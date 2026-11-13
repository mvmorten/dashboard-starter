#!/usr/bin/env bash
# Demo sandbox checks against a running API, with curl, jq and sqlcmd. CI runs this
# (.github/workflows/ci.yml) against its own API instance. You can run it locally too, after
# migrate and seed, from the repo root (Git Bash on Windows: export MSYS_NO_PATHCONV=1, and a
# TMPDIR written C:/like/this, so docker and curl both understand the paths):
#
#   SQLCMD="docker compose exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P LocalDevOnly!ChangeMe1 -C -b -I -h -1 -W -d DashboardStarter" \
#     BASE=http://localhost:5085 bash .github/scripts/sandbox-checks.sh
#
# Expects the API in Development (sandboxes on) started with Auth__SessionCheckSeconds=2,
# Demo__CleanupInterval=00:00:02 and Demo__SandboxPermitLimit raised, and $SQLCMD running
# queries as the admin login (the checks look at rows the API never shows). -I turns on
# QUOTED_IDENTIFIER, which updates to tables with filtered indexes need; SqlClient has it on.
#
# Role names appear here as test data. The code under test only ever checks permissions.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

json() { jq -r "$@" "$body"; }
sql() { $SQLCMD -Q "SET NOCOUNT ON; $1"; }
tenant_id() { json --arg name "$1" '.tenants[] | select(.name == $name) | .id'; }
load_members() {
  expect "$(status "$1" "$BASE/api/members")" 200
  cp "$body" "$work/members.json"
}
member() { jq -r --arg name "$1" ".members[] | select(.displayName == \$name) | $2" "$work/members.json"; }
role_id() { jq -r --arg name "$1" '.roles[] | select(.name == $name) | .id' "$work/members.json"; }
set_role() {
  load_members "$1"
  put "$1" "/api/members/$(member "$2" .userId)/role" \
    "{\"roleId\":$(role_id "$3"),\"rowVersion\":\"$(member "$2" .rowVersion)\"}"
}
set_status() {
  load_members "$1"
  put "$1" "/api/members/$(member "$2" .userId)/status" \
    "{\"status\":\"$3\",\"rowVersion\":\"$(member "$2" .rowVersion)\"}"
}
# The sandbox suffix in an address: visitor-1a2b3c4d5e6f@demo.invalid -> 1a2b3c4d5e6f
suffix_of() { echo "$1" | sed -n 's/^visitor-\([0-9a-f]*\)@demo\.invalid$/\1/p'; }
# Everything that isn't a demo row, to show cleanup never touches it.
real_rows() {
  sql "SELECT
    (SELECT COUNT(*) FROM dbo.Users WHERE IsDemo = 0),
    (SELECT COUNT(*) FROM dbo.Tenants WHERE IsDemo = 0),
    (SELECT COUNT(*) FROM dbo.TenantUsers AS tu JOIN dbo.Tenants AS t ON t.Id = tu.TenantId WHERE t.IsDemo = 0),
    (SELECT COUNT(*) FROM dbo.PasswordResetTokens AS p JOIN dbo.Users AS u ON u.Id = p.UserId WHERE u.IsDemo = 0),
    (SELECT CHECKSUM_AGG(CHECKSUM(Id, RoleId, Status)) FROM dbo.TenantUsers AS tu WHERE EXISTS (SELECT 1 FROM dbo.Tenants AS t WHERE t.Id = tu.TenantId AND t.IsDemo = 0));"
}
# rows_for <suffix> -> the users, tenants and memberships of one sandbox, still in the database
rows_for() {
  sql "SELECT
    (SELECT COUNT(*) FROM dbo.Users WHERE Email LIKE '%-$1@demo.invalid'),
    (SELECT COUNT(*) FROM dbo.Tenants WHERE Id IN ($2)),
    (SELECT COUNT(*) FROM dbo.TenantUsers WHERE TenantId IN ($2));"
}
wait_until_removed() {
  for attempt in $(seq 1 30); do
    if [ "$(rows_for "$1" "$2")" = "0 0 0" ]; then return 0; fi
    sleep 0.5
  done
  echo "Sandbox $1 is still there: $(rows_for "$1" "$2") (users tenants memberships)."
  exit 1
}

echo "1. Try the demo: a private user, two tenants, the sample members, and a signed-in session"
expect "$(start_demo "$work/a")" 204
expect "$(status "$work/a" "$BASE/api/auth/me")" 200
a_email=$(json .user.email)
a=$(suffix_of "$a_email")
[ -n "$a" ] || { echo "Unexpected sandbox address: $a_email"; exit 1; }
expect "$(json .user.isDemo)" true
expect "$(json '[.tenants[].name] | join(",")')" "Demo,Other Demo"
a_demo=$(tenant_id Demo)
a_other=$(tenant_id "Other Demo")
expect "$(json .currentTenantId)" "$a_demo"
expect "$(json .role)" Admin
expect "$(json '.permissions | join(",")')" "members.manage,members.view"
# Exactly these rows, all IsDemo, none with a password, all expiring together.
sql "SELECT u.Email, u.IsDemo, CASE WHEN u.PasswordHash IS NULL THEN 'none' ELSE 'set' END, t.Name, t.IsDemo, r.Name, tu.Status
  FROM dbo.TenantUsers AS tu
  JOIN dbo.Users AS u ON u.Id = tu.UserId
  JOIN dbo.Tenants AS t ON t.Id = tu.TenantId
  JOIN dbo.Roles AS r ON r.Id = tu.RoleId
  WHERE tu.TenantId IN ($a_demo, $a_other)
  ORDER BY t.Name, u.Email;" | sed "s/$a/SUFFIX/" > "$work/sandbox.txt"
cat "$work/sandbox.txt"
cat > "$work/expected.txt" <<'EOF'
jordan.blake-SUFFIX@demo.invalid 1 none Demo 1 Member Disabled
lee.chen-SUFFIX@demo.invalid 1 none Demo 1 Read-only Active
priya.shah-SUFFIX@demo.invalid 1 none Demo 1 Member Active
sam.rivera-SUFFIX@demo.invalid 1 none Demo 1 Admin Active
visitor-SUFFIX@demo.invalid 1 none Demo 1 Admin Active
priya.shah-SUFFIX@demo.invalid 1 none Other Demo 1 Member Active
sam.rivera-SUFFIX@demo.invalid 1 none Other Demo 1 Admin Active
visitor-SUFFIX@demo.invalid 1 none Other Demo 1 Read-only Active
EOF
diff "$work/expected.txt" "$work/sandbox.txt"
expect "$(sql "SELECT COUNT(DISTINCT ExpiresAt) FROM (SELECT ExpiresAt FROM dbo.Users WHERE Email LIKE '%-$a@demo.invalid' UNION ALL SELECT ExpiresAt FROM dbo.Tenants WHERE Id IN ($a_demo, $a_other)) AS e;")" 1

echo "2. A second click is a second sandbox, and neither can see the other"
expect "$(start_demo "$work/b")" 204
expect "$(status "$work/b" "$BASE/api/auth/me")" 200
b=$(suffix_of "$(json .user.email)")
b_demo=$(tenant_id Demo)
b_other=$(tenant_id "Other Demo")
if [ "$a" = "$b" ] || [ "$a_demo" = "$b_demo" ]; then echo "The second click reused the first sandbox."; exit 1; fi
expect "$(post "$work/b" /api/auth/switch-tenant "{\"tenantId\":$a_demo}")" 403
expect "$(post "$work/a" /api/auth/switch-tenant "{\"tenantId\":$b_other}")" 403
load_members "$work/a"
if grep -q "$b" "$work/members.json"; then echo "Sandbox A sees sandbox B's members."; exit 1; fi
expect "$(jq '.members | length' "$work/members.json")" 5

echo "3. In Demo the visitor is an Admin for real: member changes work"
expect "$(set_role "$work/a" "Priya Shah" Read-only)" 200
expect "$(set_status "$work/a" "Jordan Blake" Active)" 200
load_members "$work/a"
expect "$(member "Priya Shah" .roleName)" Read-only
expect "$(member "Jordan Blake" .status)" Active
echo "   Still not their own role, like anyone else"
expect "$(set_role "$work/a" "Demo Guest" Member)" 400
echo "   Sandbox B is unchanged"
load_members "$work/b"
expect "$(member "Priya Shah" .roleName)" Member
expect "$(member "Jordan Blake" .status)" Disabled

echo "4. In Other Demo the visitor is Read-only: no member changes"
expect "$(post "$work/a" /api/auth/switch-tenant "{\"tenantId\":$a_other}")" 200
expect "$(json .role)" Read-only
expect "$(json '.permissions | join(",")')" "members.view"
expect "$(set_role "$work/a" "Priya Shah" Read-only)" 403
expect "$(json .title)" "You don't have access to this"

echo "5. The Db runner won't give a demo account a password or a role"
if echo 'a-long-enough-password' | $DB set-password --email "$a_email" --password-stdin 2> "$work/refused.txt"; then
  echo "set-password gave a demo account a password."
  exit 1
fi
grep -q 'is a demo account' "$work/refused.txt"
if $DB set-role --email "$a_email" --tenant "$a_demo" --role Member 2> "$work/refused.txt"; then
  echo "set-role changed a demo account's role."
  exit 1
fi
grep -q 'is a demo account' "$work/refused.txt"

echo "6. An expired sandbox's session ends, and cleanup removes every row it owned and nothing else"
before=$(real_rows)
sql "UPDATE dbo.Users SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE Email LIKE '%-$a@demo.invalid';
  UPDATE dbo.Tenants SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE Id IN ($a_demo, $a_other);"
sleep 3
expect "$(status "$work/a" "$BASE/api/auth/me")" 401
wait_until_removed "$a" "$a_demo, $a_other"
expect "$(real_rows)" "$before"
echo "   Sandbox B is still there"
expect "$(rows_for "$b" "$b_demo, $b_other")" "5 2 8"
expect "$(status "$work/b" "$BASE/api/auth/me")" 200

echo "7. Using a sandbox keeps it alive; signing out ends it"
sql "UPDATE dbo.Users SET ExpiresAt = DATEADD(minute, 5, SYSUTCDATETIME()) WHERE Email LIKE '%-$b@demo.invalid';
  UPDATE dbo.Tenants SET ExpiresAt = DATEADD(minute, 5, SYSUTCDATETIME()) WHERE Id IN ($b_demo, $b_other);"
sleep 3
expect "$(status "$work/b" "$BASE/api/auth/me")" 200
# Moved to SandboxIdleMinutes (120) from now: well past the 5 minutes set above.
expect "$(sql "SELECT COUNT(*) FROM dbo.Tenants WHERE Id IN ($b_demo, $b_other) AND ExpiresAt > DATEADD(minute, 60, SYSUTCDATETIME());")" 2
expect "$(post "$work/b" /api/auth/sign-out)" 204
wait_until_removed "$b" "$b_demo, $b_other"

echo "8. While another instance holds the cleanup lock, this one removes nothing"
expect "$(start_demo "$work/c")" 204
expect "$(status "$work/c" "$BASE/api/auth/me")" 200
c=$(suffix_of "$(json .user.email)")
c_demo=$(tenant_id Demo)
c_other=$(tenant_id "Other Demo")
# A session that takes the same app lock and keeps it for 8 seconds, like a second instance
# halfway through a long run.
$SQLCMD -Q "DECLARE @r int; EXEC @r = sp_getapplock @Resource = N'DemoSandboxCleanup', @LockMode = 'Exclusive', @LockOwner = 'Session'; WAITFOR DELAY '00:00:08';" &
holder=$!
sleep 1
expect "$(post "$work/c" /api/auth/sign-out)" 204
sleep 5
expect "$(rows_for "$c" "$c_demo, $c_other")" "5 2 8"
wait "$holder"
wait_until_removed "$c" "$c_demo, $c_other"

echo "All sandbox checks passed."
