#!/usr/bin/env bash
# Password reset and change password checks against a running API, with curl. CI runs this
# (.github/workflows/ci.yml); you can run it locally too, against a fresh database, from the
# repo root:
#
#   bash .github/scripts/password-checks.sh          # everything except expiry
#   bash .github/scripts/password-checks.sh expiry   # needs PasswordReset__TokenLifetimeSeconds=2
#
# Expects the API in Development on $BASE (default http://localhost:5080), writing emails to
# the folder $MAIL (default .dev-mail, from appsettings.Development.json), with
# PasswordReset__PermitLimit raised so the per-IP limit doesn't get in the way, and these
# users, made with the Db runner (see the "Create users" step in ci.yml):
#   reset@example.com    any tenant          a-long-enough-password
#   change@example.com   any tenant          a-long-enough-password
#   capped@example.com   no tenant           a-long-enough-password
#   expiry@example.com   no tenant           a-long-enough-password
# and demo sandboxes on (they are in Development), for a demo account to try.
set -euo pipefail
source "$(dirname "$0")/lib.sh"
MAIL=${MAIL:-.dev-mail}
password=a-long-enough-password
invalid_link='This reset link is invalid or has expired. Request a new one.'

# How many emails the folder holds for an address (all of them, with no address).
mails() {
  if [ ! -d "$MAIL" ]; then echo 0; return; fi
  if [ -z "${1:-}" ]; then find "$MAIL" -name '*.txt' | wc -l | tr -d ' '; return; fi
  grep -lx "To: $1" "$MAIL"/*.txt 2>/dev/null | wc -l | tr -d ' '
}
# Emails go out in the background, a moment after the answer: wait until there are $2 for $1.
wait_for_mails() {
  for attempt in $(seq 1 50); do
    if [ "$(mails "$1")" -ge "$2" ]; then return 0; fi
    sleep 0.1
  done
  echo "Expected $2 emails for $1, found $(mails "$1")."
  exit 1
}
# The token from the newest email for an address.
token_for() {
  newest=$(grep -lx "To: $1" "$MAIL"/*.txt | sort | tail -1)
  sed -n 's/.*#token=\([A-Za-z0-9_-]*\).*/\1/p' "$newest" | head -1
}
forgot() { post "$work/anon" /api/auth/forgot-password "{\"email\":\"$1\"}"; }
reset() { post "$work/anon" /api/auth/reset-password "{\"token\":\"$1\",\"newPassword\":\"$2\"}"; }
change() { post "$1" /api/auth/change-password "{\"currentPassword\":\"$2\",\"newPassword\":\"$3\"}"; }
detail_is() { grep -qF "\"detail\":\"$1\"" "$body" || { echo "Expected detail: $1"; cat "$body"; echo; exit 1; }; }

if [ "${1:-}" = "expiry" ]; then
  echo "E. A reset link stops working when it expires"
  expect "$(forgot expiry@example.com)" 204
  wait_for_mails expiry@example.com 1
  token=$(token_for expiry@example.com)
  sleep 3
  expect "$(reset "$token" a-new-password-for-expiry)" 400
  detail_is "$invalid_link"
  expect "$(sign_in "$work/e" expiry@example.com "$password")" 200
  echo "Expiry check passed."
  exit 0
fi

echo "1. Forgot password answers the same for a real account, an unknown email, and a demo account"
expect "$(start_demo "$work/d")" 204
DEMO_EMAIL=$(me_email "$work/d")
before=$(mails)
expect "$(forgot reset@example.com)" 204
real=$(cat "$body")
expect "$(forgot nobody-here@example.com)" 204
expect "$(cat "$body")" "$real"
expect "$(forgot "$DEMO_EMAIL")" 204
expect "$(cat "$body")" "$real"

echo "2. Only the real account gets an email"
wait_for_mails reset@example.com 1
sleep 1
expect "$(mails)" "$((before + 1))"
expect "$(mails nobody-here@example.com)" 0
expect "$(mails "$DEMO_EMAIL")" 0
grep -q 'expires\|within 30 minutes' "$(grep -lx 'To: reset@example.com' "$MAIL"/*.txt | sort | tail -1)"

echo "3. The link resets the password; the old one stops working and open sessions end"
expect "$(sign_in "$work/before" reset@example.com "$password")" 200
token=$(token_for reset@example.com)
expect "$(reset "$token" reset-to-something-new)" 204
expect "$(sign_in "$work/x" reset@example.com "$password")" 401
expect "$(sign_in "$work/x" reset@example.com reset-to-something-new)" 200
expect "$(status "$work/before" "$BASE/api/auth/me")" 401

echo "4. The same link can't be used twice, and a made-up one gets the same answer"
expect "$(reset "$token" another-new-password)" 400
detail_is "$invalid_link"
expect "$(reset not-a-real-token-at-all another-new-password)" 400
detail_is "$invalid_link"

echo "5. Password rules: 11 characters no, 129 no, the email no, 12 yes"
expect "$(forgot reset@example.com)" 204
wait_for_mails reset@example.com 2
token=$(token_for reset@example.com)
expect "$(reset "$token" eleven-char)" 400
detail_is "The password must be at least 12 characters."
expect "$(reset "$token" "$(printf 'a%.0s' $(seq 1 129))")" 400
detail_is "The password can be at most 128 characters."
expect "$(reset "$token" RESET@example.com)" 400
detail_is "The password can't be your email address."
expect "$(reset "$token" twelve-chars)" 204
expect "$(sign_in "$work/x" reset@example.com twelve-chars)" 200

echo "6. Change password: a wrong current password fails and keeps the session"
expect "$(sign_in "$work/c1" change@example.com "$password")" 200
expect "$(sign_in "$work/c2" change@example.com "$password")" 200
expect "$(change "$work/c1" not-my-password changed-password-1)" 400
detail_is "The current password is incorrect."
expect "$(status "$work/c1" "$BASE/api/auth/me")" 200

echo "7. Change password: this session stays signed in, the other one ends"
expect "$(change "$work/c1" "$password" changed-password-1)" 204
expect "$(status "$work/c1" "$BASE/api/auth/me")" 200
expect "$(status "$work/c2" "$BASE/api/auth/me")" 401
expect "$(sign_in "$work/x" change@example.com "$password")" 401
expect "$(sign_in "$work/x" change@example.com changed-password-1)" 200
# This session can still make changes: its antiforgery token is reissued with the new cookie.
expect "$(change "$work/c1" changed-password-1 changed-password-2)" 204

echo "8. A demo account can't set a password, so a sandbox can't become a permanent account"
expect "$(change "$work/d" anything-at-all-here a-password-for-the-demo)" 403
expect "$(json_field title)" "Not for demo accounts"

echo "9. At most 3 emails per address per hour: the 4th request sends nothing"
for attempt in 1 2 3 4; do
  expect "$(forgot capped@example.com)" 204
done
wait_for_mails capped@example.com 3
sleep 1
expect "$(mails capped@example.com)" 3

echo "All password checks passed."
