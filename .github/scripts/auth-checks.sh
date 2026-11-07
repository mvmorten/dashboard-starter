#!/usr/bin/env bash
# Sign-in checks against a running API, with curl. CI runs this (.github/workflows/ci.yml);
# you can run it locally too, against a fresh database, from the repo root:
#
#   bash .github/scripts/auth-checks.sh
#
# Expects the API in Development on $BASE (default http://localhost:5080), started with
# Auth__SessionCheckSeconds=2 so a changed password shows up within seconds, and these users,
# made with the Db runner (see the "Create users" step in ci.yml):
#   ann@example.com      member of two tenants        a-long-enough-password
#   locked@example.com   any tenant                   a-long-enough-password
#   weak@example.com     hashed with Iterations=1     a-long-enough-password
#   guest@example.com    the demo account             published-demo-pass
set -euo pipefail
source "$(dirname "$0")/lib.sh"

echo "1. /me without a cookie is 401"
expect "$(status "$work/none" "$BASE/api/auth/me")" 401

echo "2. Wrong password and unknown email: 401 with the same message"
expect "$(sign_in "$work/a" ann@example.com wrong-password-123)" 401
wrong=$(grep -o '"detail":"[^"]*"' "$body")
expect "$(sign_in "$work/a" nobody@example.com wrong-password-123)" 401
# Same message; only the per-request traceId differs.
expect "$(grep -o '"detail":"[^"]*"' "$body")" "$wrong"
echo "   $wrong"

echo "3. Right password (any capitalization of the email): 200, the cookie is set, /me answers"
expect "$(sign_in "$work/a" ANN@example.com a-long-enough-password)" 200
grep -q 'ds-auth' "$work/a"
grep -qi '"email":"ann@example.com"' "$body"
current=$(field currentTenantId)
# The id of a tenant in the "tenants" list that isn't the current one.
other=$(sed -n 's/.*"tenants":\[\([^]]*\)\].*/\1/p' "$body" | grep -o '"id":[0-9]*' | sed 's/"id"://' | grep -vx "$current" | head -1)
expect "$(status "$work/a" "$BASE/api/auth/me")" 200

echo "4. The connection carries the current tenant in SESSION_CONTEXT (tenant $current)"
expect "$(status "$work/a" "$BASE/api/health")" 200
grep -q "\"sessionTenantId\":$current" "$body"
expect "$(status "$work/none" "$BASE/api/health")" 200
grep -q '"sessionTenantId":null' "$body"

echo "5. A POST without the antiforgery header is rejected"
expect "$(status "$work/a" -X POST -H 'Content-Type: application/json' -d "{\"tenantId\":$other}" "$BASE/api/auth/switch-tenant")" 400

echo "6. switch-tenant moves the session to tenant $other, and SESSION_CONTEXT with it"
expect "$(post "$work/a" /api/auth/switch-tenant "{\"tenantId\":$other}")" 200
expect "$(field currentTenantId)" "$other"
expect "$(status "$work/a" "$BASE/api/health")" 200
grep -q "\"sessionTenantId\":$other" "$body"
echo "   A tenant the user doesn't belong to is 403"
expect "$(post "$work/a" /api/auth/switch-tenant '{"tenantId":999999}')" 403

echo "7. sign-out ends the session"
expect "$(post "$work/a" /api/auth/sign-out)" 204
expect "$(status "$work/a" "$BASE/api/auth/me")" 401

echo "8. Five wrong passwords lock the account; then even the right one is refused"
for attempt in 1 2 3 4 5; do expect "$(sign_in "$work/l" locked@example.com wrong-password-123)" 401; done
expect "$(sign_in "$work/l" locked@example.com a-long-enough-password)" 401

echo "9. The demo account is never locked out"
for attempt in 1 2 3 4 5 6 7; do expect "$(sign_in "$work/d" guest@example.com wrong-password-123)" 401; done
expect "$(sign_in "$work/d" guest@example.com published-demo-pass)" 200
grep -q '"isDemo":true' "$body"

echo "10. set-password ends a session that's already open (after the session check cache expires)"
expect "$(sign_in "$work/s" ann@example.com a-long-enough-password)" 200
expect "$(status "$work/s" "$BASE/api/auth/me")" 200
echo 'a-brand-new-password' | $DB set-password --email ann@example.com --password-stdin > /dev/null
sleep 3
expect "$(status "$work/s" "$BASE/api/auth/me")" 401
expect "$(sign_in "$work/s" ann@example.com a-brand-new-password)" 200

echo "11. Signing in with a hash made with weaker settings works (ci.yml then checks it was rehashed)"
expect "$(sign_in "$work/w" weak@example.com a-long-enough-password)" 200

# Keep this one last: it uses up this address's sign-in limit, so any sign-in after it
# gets 429 until the window (Auth:SignInWindowSeconds) passes.
echo "12. Too many sign-in attempts from one address: 429 with Retry-After"
limited=no
for attempt in $(seq 1 80); do
  code=$(curl -s -o /dev/null -D "$work/headers" -w '%{http_code}' -H 'Content-Type: application/json' \
    -d '{"email":"x@example.com","password":"y"}' "$BASE/api/auth/sign-in")
  if [ "$code" = 429 ]; then limited=yes; break; fi
done
expect "$limited" yes
grep -qi '^retry-after:' "$work/headers"

echo "All auth checks passed."
