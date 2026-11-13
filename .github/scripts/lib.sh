# Shared by auth-checks.sh, password-checks.sh and role-checks.sh: curl helpers for talking to a running API
# the way the browser does (cookie jar, antiforgery token). Sourced, not run.
BASE=${BASE:-http://localhost:5080}
DB=${DB:-dotnet run --no-build --project src/Db --}
work=$(mktemp -d)
body="$work/body.json"

# The readable antiforgery cookie's value, from a curl cookie jar.
xsrf() { awk '$6 ~ /ds-xsrf$/ { print $7 }' "$1"; }
antiforgery() { curl -sf -c "$1" -b "$1" "$BASE/api/auth/antiforgery" -o /dev/null; }
# A number field from the last response body: field currentTenantId -> 2
field() { sed -n "s/.*\"$1\":\([0-9]*\).*/\1/p" "$body"; }
# status <jar> <curl args...> -> the HTTP status; the body is in $body
status() { curl -s -o "$body" -w '%{http_code}' -c "$1" -b "$1" "${@:2}"; }
# post <jar> <path> [json] -> the HTTP status, sending a fresh antiforgery token
post() {
  antiforgery "$1"
  status "$1" -X POST -H "X-XSRF-TOKEN: $(xsrf "$1")" -H 'Content-Type: application/json' -d "${3:-}" "$BASE$2"
}
# put <jar> <path> <json> -> the HTTP status, like post
put() {
  antiforgery "$1"
  status "$1" -X PUT -H "X-XSRF-TOKEN: $(xsrf "$1")" -H 'Content-Type: application/json' -d "$3" "$BASE$2"
}
sign_in() { post "$1" /api/auth/sign-in "{\"email\":\"$2\",\"password\":\"$3\"}"; }
# start_demo <jar> -> the HTTP status of "Try the demo"; on 204 the jar is signed in to a new sandbox
start_demo() { post "$1" /api/demo/sandbox; }
# A string field from the last response body: json_field title -> Not found
json_field() { sed -n "s/.*\"$1\":\"\([^\"]*\)\".*/\1/p" "$body" | head -1; }
# me_email <jar> -> the signed-in user's email, from /me
me_email() { status "$1" "$BASE/api/auth/me" > /dev/null; json_field email; }
expect() { if [ "$1" != "$2" ]; then echo "Expected $2, got $1:"; cat "$body"; echo; exit 1; fi; }
