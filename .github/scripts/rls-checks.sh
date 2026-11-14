#!/usr/bin/env bash
# Row-Level Security checks, straight against SQL Server with sqlcmd, no API involved: the policy
# covers every tenant-owned table, no tenant in SESSION_CONTEXT means no rows (fails closed), and
# one tenant's context can't read or write another tenant's rows. Run as the API's own login and
# as the admin, because neither is exempt.
#
# CI runs this (.github/workflows/ci.yml) after customer-checks.sh, which leaves customers in
# Acme Corporation and Globex Industries (including a soft-deleted one in Acme). Locally, from the
# repo root, after the same (Git Bash: export MSYS_NO_PATHCONV=1 first):
#
#   s="docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -C -b -I -h -1 -W -d DashboardStarter"
#   SQLCMD="$s -U sa -P LocalDevOnly!ChangeMe1" SQLCMD_APP="$s -U dashboard_app -P AppDevOnly!ChangeMe1" \
#     bash .github/scripts/rls-checks.sh
#
# $SQLCMD runs as the admin, $SQLCMD_APP as the API's login (made by the Db runner's app-user).
set -euo pipefail

# run <admin|app> <sql>: the output, one row per line.
run() {
  if [ "$1" = app ]; then
    $SQLCMD_APP -Q "SET NOCOUNT ON; $2"
  else
    $SQLCMD -Q "SET NOCOUNT ON; $2"
  fi
}
as_tenant() { echo "EXEC sp_set_session_context @key = N'TenantId', @value = $1;"; }
expect() { if [ "$1" != "$2" ]; then echo "Expected $2, got $1."; exit 1; fi; }
# refused <admin|app> <sql> <part of the error>: the statement must fail with that error.
refused() {
  if output=$(run "$1" "$2" 2>&1); then
    echo "Expected this to be refused, but it ran: $2"
    echo "$output"
    exit 1
  fi
  if ! echo "$output" | grep -q "$3"; then
    echo "Refused, but not with '$3': $output"
    exit 1
  fi
}

# Tables with a TenantId column that the policy doesn't fully cover: enabled, with our predicate
# as the filter and as the AFTER INSERT and AFTER UPDATE block predicates. TenantUsers is the one
# global table with a TenantId (sql/migrations/0003_tenant_users.sql), named here on purpose.
uncovered() {
  run admin "SELECT s.name + N'.' + t.name
    FROM sys.tables AS t
    INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
    INNER JOIN sys.columns AS c ON c.object_id = t.object_id AND c.name = N'TenantId'
    WHERE NOT (s.name = N'dbo' AND t.name = N'TenantUsers')
      AND (
        SELECT COUNT(DISTINCT COALESCE(sp.operation_desc, sp.predicate_type_desc))
        FROM sys.security_predicates AS sp
        INNER JOIN sys.security_policies AS p ON p.object_id = sp.object_id
        WHERE sp.target_object_id = t.object_id
          AND p.is_enabled = 1
          AND sp.predicate_definition = N'([Security].[fn_TenantAccessPredicate]([TenantId]))'
          AND (sp.predicate_type_desc = N'FILTER' OR sp.operation_desc IN (N'AFTER INSERT', N'AFTER UPDATE'))
      ) < 3
    ORDER BY 1;"
}

acme=$(run admin "SELECT Id FROM dbo.Tenants WHERE Name = N'Acme Corporation';")
globex=$(run admin "SELECT Id FROM dbo.Tenants WHERE Name = N'Globex Industries';")

echo "1. Every table with a TenantId is in the policy (TenantUsers is the one exception)"
missing=$(uncovered)
if [ -n "$missing" ]; then
  echo "Not covered by Security.TenantAccessPolicy (sql/programmable/Security.fn_TenantAccessPredicate.sql):"
  echo "$missing"
  exit 1
fi
echo "   A new table with a TenantId and no policy is caught"
run admin "CREATE TABLE dbo.ProbeRlsRows (Id int NOT NULL, TenantId int NOT NULL);"
missing=$(uncovered)
run admin "DROP TABLE dbo.ProbeRlsRows;"
expect "$missing" dbo.ProbeRlsRows

echo "2. There's data to hide: customers in both tenants (each seen in its own context)"
acme_rows=$(run admin "$(as_tenant "$acme") SELECT COUNT(*) FROM dbo.Customers;")
globex_rows=$(run admin "$(as_tenant "$globex") SELECT COUNT(*) FROM dbo.Customers;")
echo "   Acme $acme_rows, Globex $globex_rows"
[ "$acme_rows" -gt 0 ] && [ "$globex_rows" -gt 0 ]
echo "   Soft-deleted rows are still visible to their own tenant: RLS doesn't hide them, the queries do"
[ "$(run admin "$(as_tenant "$acme") SELECT COUNT(*) FROM dbo.Customers WHERE DeletedAt IS NOT NULL;")" -gt 0 ]

for who in app admin; do
  echo "3. As the $who login, with no tenant set: no rows, no inserts, no updates (fails closed)"
  expect "$(run $who "SELECT CASE WHEN SESSION_CONTEXT(N'TenantId') IS NULL THEN 'unset' ELSE 'set' END;")" unset
  expect "$(run $who "SELECT COUNT(*) FROM dbo.Customers;")" 0
  expect "$(run $who "SELECT COUNT(*) FROM dbo.Customers WHERE TenantId = $acme;")" 0
  refused $who "INSERT INTO dbo.Customers (TenantId, Name) VALUES ($acme, N'No tenant set');" "block predicate"
  expect "$(run $who "BEGIN TRAN; UPDATE dbo.Customers SET Notes = N'touched' WHERE TenantId = $acme; SELECT @@ROWCOUNT; ROLLBACK;")" 0

  echo "4. As the $who login, working in Acme: none of Globex's rows, and no writes into Globex"
  # No WHERE TenantId at all, as if a query forgot it: still only Acme's rows.
  expect "$(run $who "$(as_tenant "$acme") SELECT COUNT(*) FROM dbo.Customers;")" "$acme_rows"
  expect "$(run $who "$(as_tenant "$acme") SELECT COUNT(*) FROM dbo.Customers WHERE TenantId <> $acme;")" 0
  refused $who "$(as_tenant "$acme") INSERT INTO dbo.Customers (TenantId, Name) VALUES ($globex, N'Into Globex');" "block predicate"
  refused $who "$(as_tenant "$acme") UPDATE TOP (1) dbo.Customers SET TenantId = $globex WHERE TenantId = $acme;" "block predicate"
  expect "$(run $who "$(as_tenant "$acme") BEGIN TRAN; UPDATE dbo.Customers SET Notes = N'touched' WHERE TenantId = $globex; SELECT @@ROWCOUNT; ROLLBACK;")" 0
  expect "$(run $who "$(as_tenant "$acme") BEGIN TRAN; DELETE FROM dbo.Customers WHERE TenantId = $globex; SELECT @@ROWCOUNT; ROLLBACK;")" 0
done

echo "5. The predicate itself: true only for the tenant that's set"
expect "$(run admin "SELECT COUNT(*) FROM Security.fn_TenantAccessPredicate($acme);")" 0
expect "$(run admin "$(as_tenant "$acme") SELECT COUNT(*) FROM Security.fn_TenantAccessPredicate($acme);")" 1
expect "$(run admin "$(as_tenant "$acme") SELECT COUNT(*) FROM Security.fn_TenantAccessPredicate($globex);")" 0

echo "6. Globex's rows are all still there"
expect "$(run admin "$(as_tenant "$globex") SELECT COUNT(*) FROM dbo.Customers;")" "$globex_rows"

echo "7. The API's login can't switch the policy off"
refused app "ALTER SECURITY POLICY Security.TenantAccessPolicy WITH (STATE = OFF);" "permission"

echo "All Row-Level Security checks passed."
