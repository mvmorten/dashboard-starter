#!/usr/bin/env bash
# Roles and permissions checks against a running API, with curl and jq. CI runs this
# (.github/workflows/ci.yml) against its own API instance, so the sign-in rate limit is its own.
# You can run it locally too, against a fresh database (migrate, seed, then the users below):
#
#   BASE=http://localhost:5080 bash .github/scripts/role-checks.sh
#
# Expects the API in Development, started with Auth__SessionCheckSeconds=2, and these users
# (see the "Create users" step in ci.yml), all with the password a-long-enough-password:
#   boss@example.com    Admin in Acme Corporation
#   boss2@example.com   Admin in Acme Corporation
#   mem@example.com     Member in Acme Corporation and in Globex Industries
#   ro@example.com      Read-only in Acme Corporation
#
# Role names appear here as test data. The code under test only ever checks permissions.
set -euo pipefail
source "$(dirname "$0")/lib.sh"

PW=a-long-enough-password
ACME="Acme Corporation"

# Ids and versions out of the last response ($body), with jq.
json() { jq -r "$@" "$body"; }
tenant_id() { json --arg name "$1" '.tenants[] | select(.name == $name) | .id'; }
# load_members <jar>: GET /api/members into members.json
load_members() {
  expect "$(status "$1" "$BASE/api/members")" 200
  cp "$body" "$work/members.json"
}
member() { jq -r --arg email "$1" ".members[] | select(.email == \$email) | $2" "$work/members.json"; }
role_id() { jq -r --arg name "$1" '.roles[] | select(.name == $name) | .id' "$work/members.json"; }
# set_role <jar> <email> <role>, and set_status <jar> <email> <Active|Disabled>: the HTTP status.
# Each reads the member's current RowVersion first, as the page does.
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

echo "1. Signed out: /api/members is 401"
expect "$(status "$work/none" "$BASE/api/members")" 401

echo "2. /me lists each role's permissions in the current tenant"
expect "$(sign_in "$work/boss" boss@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.manage,members.view"
expect "$(json .role)" Admin
expect "$(sign_in "$work/mem" mem@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.view"
acme=$(tenant_id "$ACME")
globex=$(tenant_id "Globex Industries")
expect "$(json .currentTenantId)" "$acme"
expect "$(sign_in "$work/ro" ro@example.com $PW)" 200
expect "$(json '.permissions | join(",")')" "customers.view,members.view"
expect "$(json .role)" Read-only
expect "$(sign_in "$work/boss2" boss2@example.com $PW)" 200

echo "3. Every role can see the members (members.view)"
for who in ro mem boss; do
  load_members "$work/$who"
done
[ "$(member ro@example.com .roleName)" = Read-only ]
[ "$(member boss@example.com .status)" = Active ]

echo "4. Without members.manage, changes are 403 with ProblemDetails; an Admin's succeed"
expect "$(set_role "$work/ro" mem@example.com Read-only)" 403
expect "$(json .title)" "You don't have access to this"
expect "$(set_status "$work/ro" mem@example.com Disabled)" 403
expect "$(set_role "$work/mem" ro@example.com Member)" 403
expect "$(set_status "$work/mem" ro@example.com Disabled)" 403
expect "$(set_role "$work/boss" ro@example.com Member)" 200
expect "$(set_role "$work/boss" ro@example.com Read-only)" 200

echo "5. A stale RowVersion is 409, with the current one"
load_members "$work/boss"
stale=$(member ro@example.com .rowVersion)
expect "$(set_status "$work/boss" ro@example.com Disabled)" 200
expect "$(put "$work/boss" "/api/members/$(member ro@example.com .userId)/status" "{\"status\":\"Active\",\"rowVersion\":\"$stale\"}")" 409
[ -n "$(json '.rowVersion // empty')" ]
expect "$(set_status "$work/boss" ro@example.com Active)" 200

echo "6. Nobody can change their own role or disable themselves"
expect "$(set_role "$work/boss" boss@example.com Member)" 400
expect "$(set_status "$work/boss" boss@example.com Disabled)" 400

echo "7. A changed role takes effect for someone already signed in"
echo "   From the Members page: at once (their cache entry is cleared)"
expect "$(set_role "$work/mem" ro@example.com Read-only)" 403
expect "$(set_role "$work/boss" mem@example.com Admin)" 200
expect "$(set_role "$work/mem" ro@example.com Read-only)" 200
expect "$(status "$work/mem" "$BASE/api/auth/me")" 200
expect "$(json '.permissions | join(",")')" "customers.edit,customers.view,members.manage,members.view"
echo "   From the Db runner (set-role): within the session check window"
$DB set-role --email mem@example.com --tenant "$ACME" --role Member > /dev/null
sleep 3
expect "$(set_role "$work/mem" ro@example.com Read-only)" 403

echo "8. Disabling a membership ends access to that tenant only"
expect "$(set_status "$work/boss" mem@example.com Disabled)" 200
expect "$(status "$work/mem" "$BASE/api/auth/me")" 401
expect "$(sign_in "$work/mem" mem@example.com $PW)" 200
expect "$(json .currentTenantId)" "$globex"
expect "$(json '[.tenants[].id] | map(tostring) | join(",")')" "$globex"
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$acme}")" 403
load_members "$work/boss"
[ "$(member mem@example.com .status)" = Disabled ]
expect "$(set_status "$work/boss" mem@example.com Active)" 200
expect "$(post "$work/mem" /api/auth/switch-tenant "{\"tenantId\":$acme}")" 200

echo "9. Two admins demoting each other at once: one wins, and the tenant keeps an admin"
load_members "$work/boss"
member_role_id=$(role_id Member)
boss_id=$(member boss@example.com .userId)
boss_version=$(member boss@example.com .rowVersion)
boss2_id=$(member boss2@example.com .userId)
boss2_version=$(member boss2@example.com .rowVersion)
antiforgery "$work/boss"
antiforgery "$work/boss2"
demote() {
  curl -s -o "$work/race-$1.json" -w '%{http_code}' -c "$work/$1" -b "$work/$1" -X PUT \
    -H "X-XSRF-TOKEN: $(xsrf "$work/$1")" -H 'Content-Type: application/json' \
    -d "{\"roleId\":$member_role_id,\"rowVersion\":\"$3\"}" "$BASE/api/members/$2/role" > "$work/race-$1.status"
}
demote boss "$boss2_id" "$boss2_version" &
demote boss2 "$boss_id" "$boss_version" &
wait
first=$(cat "$work/race-boss.status")
second=$(cat "$work/race-boss2.status")
echo "   boss demoting boss2: $first; boss2 demoting boss: $second"
cat "$work/race-boss.json" "$work/race-boss2.json"
echo
wins=0
for code in "$first" "$second"; do
  case "$code" in
    200) wins=$((wins + 1)) ;;
    403 | 409) ;;
    *) echo "Unexpected status $code."; exit 1 ;;
  esac
done
expect "$wins" 1
# boss's session, whichever way it went: a Member can still see the list.
load_members "$work/boss"
admins=$(jq -r '[.members[] | select(.roleName == "Admin" and .status == "Active") | .email] | join(",")' "$work/members.json")
echo "   Admins left: $admins"
case "$admins" in
  boss@example.com | boss2@example.com) ;;
  *) echo "Expected exactly one of boss and boss2 to be Admin, got '$admins'."; exit 1 ;;
esac

echo "10. The Db runner can't remove the last admin either"
if $DB set-role --email "$admins" --tenant "$ACME" --role Member > "$work/set-role.txt" 2>&1; then
  echo "set-role demoted the last admin."
  exit 1
fi
grep -q "nobody who can manage members" "$work/set-role.txt"
# Put the loser back, so this script can run again on the same database.
loser=boss@example.com
if [ "$admins" = boss@example.com ]; then loser=boss2@example.com; fi
$DB set-role --email "$loser" --tenant "$ACME" --role Admin > /dev/null

echo "All role checks passed."
