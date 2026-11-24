#!/usr/bin/env bash
# Audit log checks against a running API, with curl, jq and sqlcmd: every write endpoint writes
# exactly one entry with the right action, user and changed fields; a refused request or a save
# that changes nothing writes none; a failed entry takes its change down with it (one
# transaction); restore; the history and audit reads (paging, filters, permissions); and the
# log is append-only for the API's login.
# CI runs this (.github/workflows/ci.yml) against its own API instance. Locally, after migrate,
# seed and the users below, from the repo root (Git Bash on Windows: export MSYS_NO_PATHCONV=1,
# and a TMPDIR written C:/like/this):
#
#   s="docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -C -b -I -h -1 -W -d DashboardStarter"
#   SQLCMD="$s -U sa -P LocalDevOnly!ChangeMe1" SQLCMD_APP="$s -U dashboard_app -P AppDevOnly!ChangeMe1" \
#     BASE=http://localhost:5091 bash .github/scripts/audit-checks.sh
#
# Expects the same users as customer-checks.sh (the "Create the customer check users" step in
# ci.yml), all with the password a-long-enough-password:
#   cadmin@example.com  Admin in Acme Corporation
#   cmem@example.com    Member in Acme Corporation and in Globex Industries
#   cro@example.com     Read-only in Acme Corporation
# $SQLCMD runs as the admin, to read the log directly; $SQLCMD_APP as the API's own login, to
# show what it is denied. Row-Level Security applies to both, so queries set the tenant first.
#
# Role names appear here as test data. The code under test only ever checks permissions.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

PW=a-long-enough-password
tag="au$(date +%s)"

json() { jq -r "$@" "$body"; }
sql() { $SQLCMD -Q "SET NOCOUNT ON; $1"; }
in_tenant() { sql "EXEC sp_set_session_context @key = N'TenantId', @value = $1; $2"; }
tenant_id() { json --arg name "$1" '.tenants[] | select(.name == $name) | .id'; }
user_id() { sql "SELECT Id FROM dbo.Users WHERE Email = N'$1';"; }
create() { post "$1" /api/customers "$(jq -cn --arg name "$2" --arg email "${3:-}" '{name: $name, status: "Active", email: $email}')"; }
get() { status "$1" "$BASE/api/customers/$2"; }
# update <jar> <id> <name> <rowVersion> [notes] -> the HTTP status.
update() {
  put "$1" "/api/customers/$2" "$(jq -cn --arg name "$3" --arg v "$4" --arg notes "${5:-}" '{name: $name, status: "Active", notes: $notes, rowVersion: $v}')"
}
remove() {
  antiforgery "$1"
  status "$1" -X DELETE -H "X-XSRF-TOKEN: $(xsrf "$1")" -H 'Content-Type: application/json' \
    -d "{\"rowVersion\":\"$3\"}" "$BASE/api/customers/$2"
}
restore() { post "$1" "/api/customers/$2/restore" "{\"rowVersion\":\"$3\"}"; }
# list <jar> <path> <name=value>... -> the HTTP status of a GET with those query values.
list() {
  local jar=$1 path=$2 args=()
  shift 2
  for pair in "$@"; do args+=(--data-urlencode "$pair"); done
  status "$jar" -G "${args[@]}" "$BASE$path"
}

# entries <entity type> <entity id> -> how many audit entries it has.
entries() { in_tenant "$acme" "SELECT COUNT(*) FROM dbo.AuditLog WHERE EntityType = '$1' AND EntityId = $2;"; }
# newest <entity type> <entity id> -> its newest entry as "action|user id|changed fields|", then
# old>new of the first changed field.
newest() {
  in_tenant "$acme" "SELECT TOP (1) CONCAT(Action, '|', UserId, '|',
      (SELECT STRING_AGG([key], ',') FROM OPENJSON(Changes)), '|',
      (SELECT TOP (1) CONCAT(JSON_VALUE([value], '\$.old'), '>', JSON_VALUE([value], '\$.new')) FROM OPENJSON(Changes)))
    FROM dbo.AuditLog WHERE EntityType = '$1' AND EntityId = $2 ORDER BY Id DESC;"
}
# The whole log's size, to show a refused request added nothing anywhere.
log_size() { in_tenant "$acme" "SELECT COUNT(*) FROM dbo.AuditLog;"; }
load_members() {
  expect "$(status "$1" "$BASE/api/members")" 200
  cp "$body" "$work/members.json"
}
member() { jq -r --arg email "$1" ".members[] | select(.email == \$email) | $2" "$work/members.json"; }
role_id() { jq -r --arg name "$1" '.roles[] | select(.name == $name) | .id' "$work/members.json"; }
set_role() {
  load_members "$1"
  put "$1" "/api/members/$(member "$2" .userId)/role" "{\"roleId\":$(role_id "$3"),\"rowVersion\":\"$(member "$2" .rowVersion)\"}"
}
set_status() {
  load_members "$1"
  put "$1" "/api/members/$(member "$2" .userId)/status" "{\"status\":\"$3\",\"rowVersion\":\"$(member "$2" .rowVersion)\"}"
}
# refused_sql <sql> <part of the error>: as the API's login, the statement must fail with that error.
refused_sql() {
  if output=$($SQLCMD_APP -Q "SET NOCOUNT ON; $1" 2>&1); then
    echo "Expected this to be refused, but it ran: $1"
    echo "$output"
    exit 1
  fi
  if ! echo "$output" | grep -q "$2"; then
    echo "Refused, but not with '$2': $output"
    exit 1
  fi
}

echo "1. Signed out: 401. audit.view is the Admin's only"
expect "$(status "$work/none" "$BASE/api/audit")" 401
expect "$(status "$work/none" "$BASE/api/customers/1/history")" 401
expect "$(sign_in "$work/admin" cadmin@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "audit.view,customers.edit,customers.view,members.manage,members.view"
acme=$(tenant_id "Acme Corporation")
expect "$(sign_in "$work/mem" cmem@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.view"
globex=$(tenant_id "Globex Industries")
# A sign-in starts in the tenant used last. Start in Acme whatever ran before this.
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$acme}")" 200
expect "$(sign_in "$work/ro" cro@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.view,members.view"
admin_id=$(user_id cadmin@example.com)
mem_id=$(user_id cmem@example.com)
ro_id=$(user_id cro@example.com)

echo "2. Create: one Created entry, by the signed-in user, with the fields that have a value"
expect "$(create "$work/mem" "$tag one" "one@$tag.example")" 201
id=$(json .id)
version=$(json .rowVersion)
expect "$(entries Customer "$id")" 1
expect "$(newest Customer "$id")" "Created|$mem_id|name,email,status|>$tag one"
echo "   When it happened is the database's clock"
expect "$(in_tenant "$acme" "SELECT COUNT(*) FROM dbo.AuditLog WHERE EntityId = $id AND EntityType = 'Customer' AND ABS(DATEDIFF(second, OccurredAt, SYSUTCDATETIME())) < 60;")" 1
echo "   Who and which tenant come from the session, never the request body"
expect "$(post "$work/mem" /api/customers "{\"name\":\"$tag smuggled\",\"status\":\"Active\",\"tenantId\":$globex,\"userId\":$admin_id}")" 201
expect "$(newest Customer "$(json .id)")" "Created|$mem_id|name,status|>$tag smuggled"

echo "3. Update: one Updated entry with only the fields that changed, old and new"
expect "$(update "$work/admin" "$id" "$tag one renamed" "$version")" 200
version=$(json .rowVersion)
expect "$(entries Customer "$id")" 2
# The update helper sends no email, so the email goes too: two fields changed, not five.
expect "$(newest Customer "$id")" "Updated|$admin_id|name,email|$tag one>$tag one renamed"
expect "$(update "$work/mem" "$id" "$tag one renamed" "$version" "A note")" 200
version=$(json .rowVersion)
updated_at=$(json .updatedAt)
expect "$(newest Customer "$id")" "Updated|$mem_id|notes|>A note"
expect "$(entries Customer "$id")" 3

echo "4. A save that changes nothing: 200, and nothing written (no entry, same RowVersion, same UpdatedAt)"
expect "$(update "$work/admin" "$id" "$tag one renamed" "$version" "A note")" 200
expect "$(json .rowVersion)" "$version"
expect "$(json .updatedAt)" "$updated_at"
expect "$(json .updatedByName)" "Customer Member"
expect "$(entries Customer "$id")" 3
echo "   Trimmed to the same values is still nothing"
expect "$(update "$work/admin" "$id" "  $tag one renamed  " "$version" " A note ")" 200
expect "$(json .rowVersion)" "$version"
expect "$(entries Customer "$id")" 3

echo "5. Refused requests write nothing: 409, 400, 403, 404"
before=$(log_size)
expect "$(update "$work/mem" "$id" "$tag stale" "AAAAAAAAAAE=")" 409
expect "$(remove "$work/mem" "$id" "AAAAAAAAAAE=")" 409
expect "$(restore "$work/mem" "$id" "$version")" 404
expect "$(update "$work/mem" "$id" "   " "$version")" 400
expect "$(post "$work/mem" /api/customers '{"name":"","status":"Maybe"}')" 400
expect "$(create "$work/ro" "$tag by ro")" 403
expect "$(update "$work/ro" "$id" "$tag ro changed" "$version")" 403
expect "$(remove "$work/ro" "$id" "$version")" 403
expect "$(restore "$work/ro" "$id" "$version")" 403
expect "$(update "$work/mem" 2147483647 "$tag nobody" "$version")" 404
expect "$(log_size)" "$before"
expect "$(get "$work/mem" "$id")" 200
expect "$(json .name),$(json .rowVersion)" "$tag one renamed,$version"

echo "6. Delete: one Deleted entry, with no changes"
expect "$(remove "$work/admin" "$id" "$version")" 204
expect "$(entries Customer "$id")" 4
expect "$(newest Customer "$id")" "Deleted|$admin_id||"
expect "$(in_tenant "$acme" "SELECT CASE WHEN Changes IS NULL THEN 'null' ELSE 'set' END FROM dbo.AuditLog WHERE Id = (SELECT MAX(Id) FROM dbo.AuditLog WHERE EntityType = 'Customer' AND EntityId = $id);")" null

echo "7. Deleted customers: findable with customers.edit only, and nowhere else"
expect "$(get "$work/mem" "$id")" 404
expect "$(status "$work/mem" "$BASE/api/customers/$id/deleted")" 200
expect "$(json .name),$(json .deletedByName)" "$tag one renamed,Customer Admin"
[ "$(json .deletedAt)" != null ]
deleted_version=$(json .rowVersion)
expect "$(status "$work/ro" "$BASE/api/customers/$id/deleted")" 403
expect "$(list "$work/mem" /api/customers "status=Deleted" "search=$tag")" 200
expect "$(json '[.items[].id] | map(tostring) | join(",")'),$(json .total)" "$id,1"
[ "$(json '.items[0].deletedAt')" != null ]
expect "$(list "$work/ro" /api/customers "status=Deleted")" 403
expect "$(list "$work/mem" /api/customers "search=$tag one")" 200
expect "$(json .total)" 0
echo "   A live customer isn't a deleted one"
expect "$(create "$work/mem" "$tag live")" 201
live=$(json .id)
live_version=$(json .rowVersion)
expect "$(status "$work/mem" "$BASE/api/customers/$live/deleted")" 404
expect "$(restore "$work/mem" "$live" "$live_version")" 404
expect "$(list "$work/mem" /api/customers "status=Deleted" "search=$tag")" 200
expect "$(json .total)" 1
expect "$(list "$work/mem" /api/customers "status=deleted")" 400

echo "8. Restore: 403 for Read-only, 409 on a stale RowVersion, works once, 404 the second time"
expect "$(restore "$work/ro" "$id" "$deleted_version")" 403
expect "$(restore "$work/mem" "$id" "$version")" 409
expect "$(json .title)" "Someone else changed this customer"
expect "$(json .rowVersion),$(json .current.name)" "$deleted_version,$tag one renamed"
[ "$(json .current.deletedAt)" != null ]
expect "$(entries Customer "$id")" 4
expect "$(restore "$work/mem" "$id" "$deleted_version")" 200
expect "$(json .name),$(json .deletedAt),$(json .deletedByName),$(json .updatedByName)" "$tag one renamed,null,null,Customer Member"
version=$(json .rowVersion)
expect "$(entries Customer "$id")" 5
expect "$(newest Customer "$id")" "Restored|$mem_id||"
expect "$(restore "$work/mem" "$id" "$deleted_version")" 404
expect "$(restore "$work/mem" "$id" "$version")" 404
expect "$(entries Customer "$id")" 5
echo "   Back in GET, the list and search, as it was; gone from the Deleted list"
expect "$(get "$work/ro" "$id")" 200
expect "$(json .name),$(json .notes),$(json .status)" "$tag one renamed,A note,Active"
expect "$(list "$work/mem" /api/customers "search=$tag one")" 200
expect "$(json '[.items[].id] | map(tostring) | join(",")')" "$id"
expect "$(list "$work/mem" /api/customers "status=Deleted" "search=$tag")" 200
expect "$(json .total)" 0
expect "$(in_tenant "$acme" "SELECT CONCAT(CASE WHEN DeletedAt IS NULL THEN 'live' ELSE 'deleted' END, '|', DeletedBy, '|', UpdatedBy) FROM dbo.Customers WHERE Id = $id;")" "live||$mem_id"

echo "9. One transaction: when the entry can't be written, the change isn't made either"
# A constraint only this check has, added as the admin: no entry can be inserted while it's there.
sql "ALTER TABLE dbo.AuditLog WITH NOCHECK ADD CONSTRAINT CK_AuditLog_Probe CHECK (Action = 'Probe');"
drop_probe() { sql "ALTER TABLE dbo.AuditLog DROP CONSTRAINT IF EXISTS CK_AuditLog_Probe;"; }
trap drop_probe EXIT
before=$(log_size)
customers_before=$(in_tenant "$acme" "SELECT COUNT(*) FROM dbo.Customers;")
expect "$(update "$work/mem" "$id" "$tag must not stick" "$version")" 500
expect "$(create "$work/mem" "$tag must not exist")" 500
expect "$(remove "$work/mem" "$id" "$version")" 500
expect "$(set_role "$work/admin" cro@example.com Member)" 500
drop_probe
trap - EXIT
expect "$(log_size)" "$before"
expect "$(in_tenant "$acme" "SELECT COUNT(*) FROM dbo.Customers;")" "$customers_before"
expect "$(get "$work/mem" "$id")" 200
expect "$(json .name),$(json .rowVersion)" "$tag one renamed,$version"
load_members "$work/admin"
expect "$(member cro@example.com .roleName)" Read-only

echo "10. Members: a role or status change writes one entry; a refused one, none"
before=$(entries Member "$ro_id")
expect "$(set_role "$work/admin" cro@example.com Member)" 200
expect "$(entries Member "$ro_id")" $((before + 1))
expect "$(newest Member "$ro_id")" "Updated|$admin_id|role|Read-only>Member"
expect "$(set_status "$work/admin" cro@example.com Disabled)" 200
expect "$(newest Member "$ro_id")" "Updated|$admin_id|status|Active>Disabled"
expect "$(set_status "$work/admin" cro@example.com Active)" 200
expect "$(newest Member "$ro_id")" "Updated|$admin_id|status|Disabled>Active"
expect "$(entries Member "$ro_id")" $((before + 3))
echo "    The role they already have: no entry"
expect "$(set_role "$work/admin" cro@example.com Member)" 200
expect "$(entries Member "$ro_id")" $((before + 3))
echo "    Refused: no permission (403), their own membership (400), a stale RowVersion (409)"
size=$(log_size)
expect "$(set_role "$work/mem" cro@example.com Admin)" 403
expect "$(set_role "$work/admin" cadmin@example.com Member)" 400
load_members "$work/admin"
expect "$(put "$work/admin" "/api/members/$ro_id/status" '{"status":"Disabled","rowVersion":"AAAAAAAAAAE="}')" 409
expect "$(log_size)" "$size"
echo "    The Db runner's set-role: an entry with no user"
$DB set-role --email cro@example.com --tenant "Acme Corporation" --role Read-only > /dev/null
expect "$(entries Member "$ro_id")" $((before + 4))
expect "$(newest Member "$ro_id")" "Updated||role|Member>Read-only"
# The API reads the role again within its session check; sign in again rather than wait.
expect "$(sign_in "$work/ro" cro@example.com $PW)" 200

echo "11. A customer's history: newest first, who by name, for anyone who can see the customer"
expect "$(status "$work/ro" "$BASE/api/customers/$id/history")" 200
expect "$(json '[.items[].action] | join(",")'),$(json .total)" "Restored,Deleted,Updated,Updated,Created,5"
expect "$(json '[.items[].userName] | join(",")')" "Customer Member,Customer Admin,Customer Member,Customer Admin,Customer Member"
expect "$(json '.items[2].changes | keys | join(",")')" "notes"
expect "$(json '.items[2].changes.notes.old'),$(json '.items[2].changes.notes.new')" "null,A note"
expect "$(json '.items[0].changes'),$(json '.items[0].entityType'),$(json '.items[0].entityId')" "null,Customer,$id"
echo "    A deleted customer still has one, for those who can see deleted customers (customers.edit);"
echo "    for Read-only it's a 404, like the customer. So is an unknown one, or another tenant's"
expect "$(get "$work/mem" "$live")" 200
expect "$(remove "$work/mem" "$live" "$(json .rowVersion)")" 204
expect "$(status "$work/mem" "$BASE/api/customers/$live/history")" 200
expect "$(json '[.items[].action] | join(",")')" "Deleted,Created"
expect "$(status "$work/ro" "$BASE/api/customers/$live/history")" 404
expect "$(status "$work/mem" "$BASE/api/customers/2147483647/history")" 404
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$globex}")" 200
expect "$(create "$work/mem" "$tag in globex")" 201
globex_customer=$(json .id)
expect "$(status "$work/mem" "$BASE/api/customers/$id/history")" 404
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$acme}")" 200
expect "$(status "$work/mem" "$BASE/api/customers/$globex_customer/history")" 404
echo "    Paging: 25 a page, every entry once, in one order"
expect "$(create "$work/mem" "$tag paged 00")" 201
paged=$(json .id)
version=$(json .rowVersion)
for n in $(seq -w 1 26); do
  expect "$(update "$work/mem" "$paged" "$tag paged $n" "$version")" 200
  version=$(json .rowVersion)
done
expect "$(status "$work/mem" "$BASE/api/customers/$paged/history")" 200
expect "$(json .total),$(json .pageSize),$(json '.items | length'),$(json '.items[0].changes.name.new')" "27,25,25,$tag paged 26"
first=$(json '[.items[].id] | map(tostring) | join(" ")')
expect "$(list "$work/mem" "/api/customers/$paged/history" "page=2")" 200
expect "$(json '.items | length'),$(json '.items[1].action')" "2,Created"
all="$first $(json '[.items[].id] | map(tostring) | join(" ")')"
expect "$all" "$(tr ' ' '\n' <<< "$all" | sort -rnu | paste -sd ' ')"
expect "$(list "$work/mem" "/api/customers/$paged/history" "page=3")" 200
expect "$(json '.items | length'),$(json .total)" "0,27"
expect "$(list "$work/mem" "/api/customers/$paged/history" "page=0")" 400
expect "$(list "$work/mem" "/api/customers/$paged/history" "page=two")" 400

echo "12. The tenant's audit log: audit.view only, newest first, filters from fixed lists"
expect "$(status "$work/mem" "$BASE/api/audit")" 403
expect "$(status "$work/ro" "$BASE/api/audit")" 403
expect "$(status "$work/admin" "$BASE/api/audit")" 200
expect "$(json .pageSize),$(json '.items | length')" "25,25"
ids=$(json '[.items[].id] | map(tostring) | join(" ")')
expect "$ids" "$(tr ' ' '\n' <<< "$ids" | sort -rnu | paste -sd ' ')"
expect "$(json '.items[0].entityName'),$(json '.items[0].entityDeleted')" "$tag paged 26,false"
echo "    Only this tenant's: what the same member did in Globex isn't counted"
expect "$(list "$work/admin" /api/audit "userId=$mem_id")" 200
expect "$(json .total)" "$(in_tenant "$acme" "SELECT COUNT(*) FROM dbo.AuditLog WHERE UserId = $mem_id;")"
[ "$(in_tenant "$globex" "SELECT COUNT(*) FROM dbo.AuditLog WHERE UserId = $mem_id AND EntityId = $globex_customer;")" = 1 ]
expect "$(json '[.items[].userId] | unique | map(tostring) | join(",")')" "$mem_id"
echo "    By entity type, with the member's name, and the deleted customer marked deleted"
expect "$(list "$work/admin" /api/audit "entityType=Member")" 200
expect "$(json '[.items[].entityType] | unique | join(",")')" "Member"
expect "$(json '.items[0].entityId'),$(json '.items[0].entityName'),$(json '.items[0].userId'),$(json '.items[0].userName')" "$ro_id,Customer Reader,null,null"
expect "$(json '.items[1].userName'),$(json '.items[1].changes.status.new')" "Customer Admin,Active"
expect "$(list "$work/admin" /api/audit "entityType=Customer" "userId=$mem_id" "page=2")" 200
expect "$(json '[.items[].entityType] | unique | join(",")'),$(json .page)" "Customer,2"
expect "$(json --argjson id "$live" '[.items[] | select(.entityId == $id) | .entityDeleted] | unique | join(",")')" "true"
expect "$(list "$work/admin" /api/audit "entityType=Member" "userId=$mem_id")" 200
expect "$(json .total)" 0
echo "    An empty value is the same as leaving it out"
expect "$(list "$work/admin" /api/audit "entityType=" "userId=")" 200
[ "$(json .total)" -gt 30 ]
echo "    Unknown values are a 400 that names them"
for bad in "entityType=customer" "entityType=Bogus" "entityType=Customer' OR 1=1 --" "userId=0" "userId=-1" "userId=sam" "page=0"; do
  expect "$(list "$work/admin" /api/audit "$bad")" 400
  expect "$(json '.errors | keys | join(",")')" "${bad%%=*}"
done

echo "13. Append-only: the API's login can insert and read, never update or delete"
refused_sql "EXEC sp_set_session_context @key = N'TenantId', @value = $acme; UPDATE dbo.AuditLog SET Action = 'Deleted' WHERE EntityId = $id;" "UPDATE permission was denied"
refused_sql "EXEC sp_set_session_context @key = N'TenantId', @value = $acme; DELETE FROM dbo.AuditLog WHERE EntityId = $id;" "DELETE permission was denied"
refused_sql "EXEC sp_set_session_context @key = N'TenantId', @value = $acme; TRUNCATE TABLE dbo.AuditLog;" "permission"
echo "    The cleanup's procedure only works for an expired demo tenant"
size=$(log_size)
refused_sql "EXEC sp_set_session_context @key = N'TenantId', @value = $acme; EXEC dbo.DeleteDemoTenantAuditLog @TenantId = $acme;" "only deleted with an expired demo tenant"
expect "$(start_demo "$work/demo")" 204
expect "$(status "$work/demo" "$BASE/api/auth/me")" 200
demo=$(json .currentTenantId)
refused_sql "EXEC sp_set_session_context @key = N'TenantId', @value = $demo; EXEC dbo.DeleteDemoTenantAuditLog @TenantId = $demo;" "only deleted with an expired demo tenant"
expect "$(log_size)" "$size"
[ "$(sql "EXEC sp_set_session_context @key = N'TenantId', @value = $demo; SELECT COUNT(*) FROM dbo.AuditLog;")" -gt 0 ]
expect "$(post "$work/demo" /api/auth/sign-out)" 204

echo "All audit checks passed."
