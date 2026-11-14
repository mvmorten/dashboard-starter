#!/usr/bin/env bash
# Customers API checks against a running API, with curl, jq and sqlcmd: each role gets exactly its
# permissions, validation, paging and search (with LIKE's wildcards taken literally), edit
# conflicts (409 with the current record), soft delete, tenant isolation, and the demo row cap.
# CI runs this (.github/workflows/ci.yml) against its own API instance. Locally, after migrate,
# seed and the users below, from the repo root (Git Bash on Windows: export MSYS_NO_PATHCONV=1,
# and a TMPDIR written C:/like/this):
#
#   SQLCMD="docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P LocalDevOnly!ChangeMe1 -C -b -I -h -1 -W -d DashboardStarter" \
#     BASE=http://localhost:5088 bash .github/scripts/customer-checks.sh
#
# Expects the API in Development (sandboxes on), and these users (the "Create users" step in
# ci.yml), all with the password a-long-enough-password:
#   cadmin@example.com  Admin in Acme Corporation
#   cmem@example.com    Member in Acme Corporation and in Globex Industries
#   cro@example.com     Read-only in Acme Corporation
# $SQLCMD runs as the admin, to look at rows the API never shows (soft-deleted ones). Row-Level
# Security applies to the admin too, so every query on Customers sets the tenant first.
#
# Role names appear here as test data. The code under test only ever checks permissions.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

PW=a-long-enough-password
# Names in this run start with this, so the checks don't depend on what's already there.
tag="ck$(date +%s)"

json() { jq -r "$@" "$body"; }
sql() { $SQLCMD -Q "SET NOCOUNT ON; $1"; }
# in_tenant <tenant id> <sql>: run as the admin, working in that tenant.
in_tenant() { sql "EXEC sp_set_session_context @key = N'TenantId', @value = $1; $2"; }
tenant_id() { json --arg name "$1" '.tenants[] | select(.name == $name) | .id'; }
user_id() { sql "SELECT Id FROM dbo.Users WHERE Email = N'$1';"; }
# fields <name> [status] [email]: a customer as JSON.
fields() { jq -cn --arg name "$1" --arg status "${2:-Active}" --arg email "${3:-}" '{name: $name, status: $status, email: $email}'; }
# create <jar> <name> [status] [email] -> the HTTP status; the new customer is in $body.
create() { post "$1" /api/customers "$(fields "$2" "${3:-Active}" "${4:-}")"; }
get() { status "$1" "$BASE/api/customers/$2"; }
# search <jar> <text> [page] -> the HTTP status; the page is in $body.
search() { status "$1" -G --data-urlencode "search=$2" --data-urlencode "page=${3:-1}" "$BASE/api/customers"; }
# update <jar> <id> <name> <rowVersion> -> the HTTP status.
update() {
  put "$1" "/api/customers/$2" "$(jq -cn --arg name "$3" --arg v "$4" '{name: $name, status: "Active", rowVersion: $v}')"
}
# remove <jar> <id> <rowVersion> -> the HTTP status of DELETE, which takes the RowVersion in its body.
remove() {
  antiforgery "$1"
  status "$1" -X DELETE -H "X-XSRF-TOKEN: $(xsrf "$1")" -H 'Content-Type: application/json' \
    -d "{\"rowVersion\":\"$3\"}" "$BASE/api/customers/$2"
}
names() { json '[.items[].name] | join(",")'; }

echo "1. Signed out: 401"
expect "$(status "$work/none" "$BASE/api/customers")" 401
expect "$(get "$work/none" 1)" 401

echo "2. Each role's permissions in /me"
expect "$(sign_in "$work/admin" cadmin@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.manage,members.view"
expect "$(sign_in "$work/mem" cmem@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.view"
acme=$(tenant_id "Acme Corporation")
globex=$(tenant_id "Globex Industries")
expect "$(json .currentTenantId)" "$acme"
expect "$(sign_in "$work/ro" cro@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.view,members.view"

echo "3. Admin and Member can add, change and delete; Read-only can only look"
for who in admin mem; do
  expect "$(create "$work/$who" "$tag $who")" 201
  id=$(json .id)
  version=$(json .rowVersion)
  expect "$(get "$work/$who" "$id")" 200
  expect "$(update "$work/$who" "$id" "$tag $who changed" "$version")" 200
  expect "$(json .name)" "$tag $who changed"
  expect "$(remove "$work/$who" "$id" "$(json .rowVersion)")" 204
done
expect "$(create "$work/mem" "$tag for ro")" 201
id=$(json .id)
version=$(json .rowVersion)
expect "$(status "$work/ro" "$BASE/api/customers")" 200
expect "$(get "$work/ro" "$id")" 200
expect "$(json .name)" "$tag for ro"
expect "$(create "$work/ro" "$tag by ro")" 403
expect "$(update "$work/ro" "$id" "$tag ro changed" "$version")" 403
expect "$(remove "$work/ro" "$id" "$version")" 403
expect "$(get "$work/ro" "$id")" 200
expect "$(json .name)" "$tag for ro"

echo "4. Who and which tenant come from the session, never the request body"
mem_id=$(user_id cmem@example.com)
expect "$(post "$work/mem" /api/customers \
  "{\"name\":\"$tag smuggled\",\"status\":\"Active\",\"tenantId\":$globex,\"createdBy\":1,\"updatedBy\":1}")" 201
id=$(json .id)
expect "$(in_tenant "$acme" "SELECT CONCAT(TenantId, ' ', CreatedBy, ' ', UpdatedBy) FROM dbo.Customers WHERE Id = $id;")" "$acme $mem_id $mem_id"

echo "5. Validation: ProblemDetails with field errors; lengths match the columns"
expect "$(post "$work/mem" /api/customers '{"name":"   ","email":"not-an-email","status":"Maybe"}')" 400
expect "$(json '.errors | keys | join(",")')" "email,name,status"
long() { printf "x%.0s" $(seq 1 "$1"); }
expect "$(post "$work/mem" /api/customers "{\"name\":\"$(long 201)\",\"status\":\"Active\"}")" 400
expect "$(json '.errors | keys | join(",")')" "name"
expect "$(post "$work/mem" /api/customers "{\"name\":\"$tag $(long 150)\",\"status\":\"Active\",\"phone\":\"$(long 51)\",\"notes\":\"$(long 2001)\"}")" 400
expect "$(json '.errors | keys | join(",")')" "notes,phone"
echo "   At the limits, and trimmed, with empty optional fields stored as null"
expect "$(post "$work/mem" /api/customers \
  "{\"name\":\"  $tag $(long 180)  \",\"status\":\"Inactive\",\"email\":\"  \",\"phone\":\"$(long 50)\",\"notes\":\"$(long 2000)\"}")" 201
expect "$(json '.name | length')" $((${#tag} + 181))
expect "$(json '.email // "null"')" null
expect "$(json .status)" Inactive

echo "6. Paging: 25 a page, sorted by name then id, with the total"
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$globex}")" 200
for n in $(seq -w 1 25); do
  expect "$(create "$work/mem" "pg$tag $n")" 201
done
expect "$(create "$work/mem" "pg$tag dup")" 201
first_dup=$(json .id)
expect "$(create "$work/mem" "pg$tag dup")" 201
second_dup=$(json .id)
expect "$(search "$work/mem" "pg$tag")" 200
expect "$(json .total)" 27
expect "$(json .pageSize)" 25
expect "$(json '.items | length')" 25
expect "$(json '.items[0].name')" "pg$tag 01"
expect "$(json '.items[24].name')" "pg$tag 25"
expect "$(search "$work/mem" "pg$tag" 2)" 200
expect "$(json '[.items[].id] | map(tostring) | join(",")')" "$first_dup,$second_dup"
expect "$(search "$work/mem" "pg$tag" 3)" 200
expect "$(json '.items | length'),$(json .total)" "0,27"
expect "$(search "$work/mem" "pg$tag" 0)" 400

echo "7. Search matches the start of the name or email; %, _ and [ match only themselves"
for name in "$tag 50% off" "$tag 500 club" "$tag a_b" "$tag axb" "$tag [x] co" "$tag x co"; do
  expect "$(create "$work/mem" "$name")" 201
done
expect "$(create "$work/mem" "Mail Only $tag" Active "$tag.box@mail.example")" 201
expect "$(search "$work/mem" "$tag 50%")" 200
expect "$(names)" "$tag 50% off"
expect "$(search "$work/mem" "$tag a_")" 200
expect "$(names)" "$tag a_b"
expect "$(search "$work/mem" "$tag [x")" 200
expect "$(names)" "$tag [x] co"
expect "$(search "$work/mem" "$tag.BOX@")" 200
expect "$(names)" "Mail Only $tag"
echo "   Not the middle of a name"
expect "$(search "$work/mem" "50% off")" 200
expect "$(json .total)" 0

echo "8. Tenants are separate: Globex's customer isn't there from Acme"
expect "$(search "$work/mem" "$tag 50%")" 200
globex_customer=$(json '.items[0].id')
expect "$(get "$work/mem" "$globex_customer")" 200
globex_version=$(json .rowVersion)
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$acme}")" 200
expect "$(get "$work/mem" "$globex_customer")" 404
expect "$(update "$work/mem" "$globex_customer" "$tag hijacked" "$globex_version")" 404
expect "$(remove "$work/mem" "$globex_customer" "$globex_version")" 404
expect "$(search "$work/mem" "$tag 50%")" 200
expect "$(json .total)" 0
expect "$(in_tenant "$globex" "SELECT Name FROM dbo.Customers WHERE Id = $globex_customer AND DeletedAt IS NULL;")" "$tag 50% off"

echo "9. A stale RowVersion: 409 with the current record on PUT and DELETE"
expect "$(create "$work/mem" "$tag conflict")" 201
id=$(json .id)
stale=$(json .rowVersion)
expect "$(update "$work/admin" "$id" "$tag theirs" "$stale")" 200
fresh=$(json .rowVersion)
expect "$(update "$work/mem" "$id" "$tag mine" "$stale")" 409
expect "$(json .title)" "Someone else changed this customer"
expect "$(json .rowVersion)" "$fresh"
expect "$(json .current.rowVersion)" "$fresh"
expect "$(json .current.name)" "$tag theirs"
expect "$(json .current.updatedByName)" "Customer Admin"
expect "$(remove "$work/mem" "$id" "$stale")" 409
expect "$(json .current.name)" "$tag theirs"
expect "$(get "$work/mem" "$id")" 200
expect "$(json .name)" "$tag theirs"

echo "10. Soft delete: gone from GET, PUT, DELETE, the list and search; the row stays, with who and when"
expect "$(create "$work/mem" "$tag deleted")" 201
id=$(json .id)
version=$(json .rowVersion)
expect "$(remove "$work/mem" "$id" "$version")" 204
expect "$(get "$work/mem" "$id")" 404
expect "$(json .title)" "No such customer"
expect "$(update "$work/mem" "$id" "$tag back" "$version")" 404
expect "$(remove "$work/mem" "$id" "$version")" 404
expect "$(search "$work/mem" "$tag deleted")" 200
expect "$(json .total)" 0
expect "$(status "$work/mem" "$BASE/api/customers")" 200
if json '.items[].id' | grep -qx "$id"; then echo "The deleted customer is still in the list."; exit 1; fi
expect "$(in_tenant "$acme" "SELECT CONCAT(Name, '|', DeletedBy, '|', CASE WHEN DeletedAt IS NULL THEN 'live' ELSE 'deleted' END) FROM dbo.Customers WHERE Id = $id;")" \
  "$tag deleted|$mem_id|deleted"
echo "    And a customer deleted while someone edits it: their save is a 404, not a 409"
expect "$(create "$work/mem" "$tag deleted while editing")" 201
id=$(json .id)
version=$(json .rowVersion)
expect "$(remove "$work/admin" "$id" "$version")" 204
expect "$(update "$work/mem" "$id" "$tag my edit" "$version")" 404

echo "11. The demo row cap: 200 customers, deleted ones counted; real tenants have none"
expect "$(start_demo "$work/demo")" 204
expect "$(status "$work/demo" "$BASE/api/auth/me")" 200
demo=$(json .currentTenantId)
expect "$(status "$work/demo" "$BASE/api/customers")" 200
expect "$(json .total)" 30
# Up to 199 rows, the extra ones already deleted (as create-then-delete would leave them).
in_tenant "$demo" "INSERT INTO dbo.Customers (TenantId, Name, DeletedAt)
  SELECT TOP (169) $demo, N'Deleted filler', SYSUTCDATETIME() FROM sys.all_objects;"
expect "$(in_tenant "$demo" "SELECT COUNT(*) FROM dbo.Customers;")" 199
expect "$(create "$work/demo" "Customer 200")" 201
expect "$(create "$work/demo" "Customer 201")" 422
expect "$(json .title)" "This demo has 200 customers"
expect "$(in_tenant "$demo" "SELECT COUNT(*) FROM dbo.Customers;")" 200
echo "    Acme isn't a demo tenant: no cap"
acme_rows=$(in_tenant "$acme" "SELECT COUNT(*) FROM dbo.Customers;")
if [ "$acme_rows" -lt 200 ]; then
  in_tenant "$acme" "INSERT INTO dbo.Customers (TenantId, Name, DeletedAt)
    SELECT TOP ($((200 - acme_rows))) $acme, N'Deleted filler', SYSUTCDATETIME() FROM sys.all_objects;"
fi
expect "$(create "$work/mem" "$tag past 200")" 201

echo "All customer checks passed."
