#!/usr/bin/env bash
# Tests for the Linux installers' download policy: check_download_url in
# docs/install.sh and docs/install-system.sh, and the HTTPS-only fallback in
# docs/install-lib.sh's download_and_verify.
#
# The policy: downloads are HTTPS-only (curl limited to https for the request
# and every redirect, TLS 1.2+). Plain http:// is accepted only for a host on
# this machine (redirects must still be HTTPS), or for any host when
# COLLABHOST_INSTALL_ALLOW_HTTP=1 is set. Everything else is refused.
#
# What this checks:
#   1. The two copies of check_download_url are identical.
#   2. A table of URLs, including spellings built to slip a remote host past a
#      loopback check, gets the expected verdict.
#   3. install-lib.sh, sourced by a script that does not define the function
#      (an installer saved from an earlier release), still refuses plain HTTP.
#   4. The curl on this machine actually enforces the flags the policy hands
#      it: a local server redirecting http -> http is refused under the
#      loopback flags and allowed under the opt-in flags.
#
# Usage: tools/test-install-download-policy.sh   (exit 0 = all passed)
# Needs bash, awk, curl and python3. No network access.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
USER_SCRIPT="${REPO_ROOT}/docs/install.sh"
SYSTEM_SCRIPT="${REPO_ROOT}/docs/install-system.sh"
LIB="${REPO_ROOT}/docs/install-lib.sh"

WORK="$(mktemp -d)"
SERVER_PID=""
cleanup() {
  if [ -n "${SERVER_PID}" ]; then kill "${SERVER_PID}" 2>/dev/null || true; fi
  rm -rf "${WORK}"
}
trap cleanup EXIT

FAILED=0
PASSED=0
pass() { PASSED=$((PASSED + 1)); echo "  ok: $1"; }
fail() { FAILED=$((FAILED + 1)); echo "  FAIL: $1" >&2; }

extract_fn() {
  awk '/^check_download_url\(\) \{$/ { f = 1 } f { print } f && /^\}$/ { exit }' "$1"
}

# ---- 1. The two copies are identical -----------------------------------------

echo "== 1. check_download_url is identical in both installers"
extract_fn "${USER_SCRIPT}" > "${WORK}/fn-user.sh"
extract_fn "${SYSTEM_SCRIPT}" > "${WORK}/fn-system.sh"
if [ ! -s "${WORK}/fn-user.sh" ] || [ ! -s "${WORK}/fn-system.sh" ]; then
  fail "could not extract check_download_url from both scripts"
elif cmp -s "${WORK}/fn-user.sh" "${WORK}/fn-system.sh"; then
  pass "identical ($(wc -l < "${WORK}/fn-user.sh") lines)"
else
  fail "the copies differ:"
  diff -u "${WORK}/fn-user.sh" "${WORK}/fn-system.sh" >&2 || true
fi

# ---- 2. Verdict table ---------------------------------------------------------

echo "== 2. URL verdicts"

# Prints one of: https, loopback, optin, refused.
verdict() {
  local url="$1" optin="$2" out
  if out="$(
    COLLABHOST_INSTALL_ALLOW_HTTP="${optin}" bash -c '
      set -euo pipefail
      . "$1"
      check_download_url TEST "$2" 2>/dev/null
      printf "%s" "${CURL_PROTO_ARGS[*]}"
    ' _ "${WORK}/fn-user.sh" "${url}"
  )"; then
    case "${out}" in
      "--proto =https --proto-redir =https --tlsv1.2")           echo https ;;
      "--proto =http,https --proto-redir =https --tlsv1.2")      echo loopback ;;
      "--proto =http,https --proto-redir =http,https --tlsv1.2") echo optin ;;
      *)                                                         echo "unexpected:${out}" ;;
    esac
  else
    echo refused
  fi
}

check() {
  local expected="$1" url="$2" optin="${3:-}" got
  got="$(verdict "${url}" "${optin}")"
  if [ "${got}" = "${expected}" ]; then
    pass "${expected}: '${url}'${optin:+ (ALLOW_HTTP=${optin})}"
  else
    fail "'${url}'${optin:+ (ALLOW_HTTP=${optin})}: expected ${expected}, got ${got}"
  fi
}

check https    "https://github.com/MrBildo/collabhost/releases/download/v1.9.0"
check https    "https://mirror.example.com:8443/collabhost"
check loopback "http://localhost:9000/releases/v1.9.0"
check loopback "http://LOCALHOST/x"
check loopback "http://127.0.0.1:8080"
check loopback "http://127.1.2.3/x"
check loopback "http://[::1]:9000/x"
check loopback "http://user:secret@localhost/x"
check refused  "http://mirror.example.com/collabhost"
check optin    "http://mirror.example.com/collabhost" 1
check refused  "http://mirror.example.com/collabhost" yes
# Spellings that put a remote host behind something that looks local.
check refused  "http://127.0.0.1.nip.io/x"
check refused  "http://evil.127.0.0.1/x"
check refused  "http://localhost.example.com/x"
check refused  "http://127.0.0.1@example.com/x"
check refused  "http://localhost:80@example.com/x"
check refused  "http://example.com#@127.0.0.1/"
check refused  "http://example.com?@127.0.0.1/"
check refused  "http://example.com/path@127.0.0.1"
check refused  "http://[::ffff:127.0.0.1]/x"
check refused  "http://0x7f000001/x"
# Not http(s) at all, opt-in or not.
check refused  "ftp://example.com/x"
check refused  "ftp://example.com/x" 1
check refused  "file:///tmp/archive"
check refused  "HTTPS://github.com/x"
check refused  ""

# ---- 3. install-lib.sh without the caller's function --------------------------

echo "== 3. install-lib.sh fallback when the calling script predates the policy"

lib_fallback() {
  # Stub curl so nothing downloads; record whether it was called and with what.
  bash -c '
    set -euo pipefail
    CURL_LOG="$3"
    curl() { printf "%s\n" "$*" >> "${CURL_LOG}"; return 0; }
    . "$1"
    TMP_DIR="$(mktemp -d)"
    ARCHIVE="collabhost-1.0.0-linux-x64.tar.gz"
    ARCHIVE_URL="$2/${ARCHIVE}"
    CHECKSUMS_URL="$2/checksums.txt"
    SHA_CMD=sha256sum
    TAG=v1.0.0
    REPO=MrBildo/collabhost
    download_and_verify
  ' _ "${LIB}" "$1" "$2" >/dev/null 2>"$2.err" || true
}

: > "${WORK}/curl-http.log"
lib_fallback "http://localhost:9000/r" "${WORK}/curl-http.log"
if [ ! -s "${WORK}/curl-http.log" ] && grep -q 'Refusing COLLABHOST_INSTALL_BASE_URL' "${WORK}/curl-http.log.err"; then
  pass "plain HTTP refused before any curl call"
else
  fail "plain HTTP was not refused by the fallback (curl calls: $(wc -l < "${WORK}/curl-http.log"))"
fi

: > "${WORK}/curl-https.log"
lib_fallback "https://example.com/r" "${WORK}/curl-https.log"
if [ "$(grep -c -- '--proto =https --proto-redir =https --tlsv1.2' "${WORK}/curl-https.log")" -eq 3 ]; then
  pass "HTTPS allowed; all 3 curl calls (HEAD, archive, checksums) carry the https-only flags"
else
  fail "HTTPS fallback: expected 3 curl calls with https-only flags, got: $(tr '\n' '|' < "${WORK}/curl-https.log")"
fi

# ---- 4. curl enforces the flags -----------------------------------------------

echo "== 4. curl on this machine enforces the redirect flags"

cat > "${WORK}/redirect-server.py" <<'PY'
import http.server, socketserver, sys

class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        port = self.server.server_address[1]
        if self.path == "/redirect":
            self.send_response(302)
            self.send_header("Location", "http://127.0.0.1:%d/target" % port)
            self.end_headers()
        else:
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"reached")

with socketserver.TCPServer(("127.0.0.1", 0), H) as s:
    open(sys.argv[1], "w").write(str(s.server_address[1]))
    s.serve_forever()
PY
python3 "${WORK}/redirect-server.py" "${WORK}/port" &
SERVER_PID=$!
for _ in $(seq 1 50); do [ -s "${WORK}/port" ] && break; sleep 0.1; done
PORT="$(cat "${WORK}/port" 2>/dev/null || true)"
if [ -z "${PORT}" ]; then
  fail "redirect server did not start"
else
  BASE="http://127.0.0.1:${PORT}"
  # Control: the server and the plain request work (otherwise every refusal below is meaningless).
  if [ "$(curl -fsS --max-time 5 "${BASE}/target" 2>/dev/null)" = "reached" ]; then
    pass "control: the local server answers"
  else
    fail "control: the local server did not answer"
  fi
  if curl --proto '=http,https' --proto-redir '=https' --tlsv1.2 -fsSL --max-time 5 "${BASE}/redirect" -o /dev/null 2>/dev/null; then
    fail "loopback flags followed a redirect to plain HTTP"
  else
    pass "loopback flags refuse a redirect to plain HTTP"
  fi
  if [ "$(curl --proto '=http,https' --proto-redir '=http,https' --tlsv1.2 -fsSL --max-time 5 "${BASE}/redirect" 2>/dev/null)" = "reached" ]; then
    pass "opt-in flags follow the same redirect (so the refusal above is the flag, not the server)"
  else
    fail "opt-in flags did not follow the redirect"
  fi
  if curl --proto '=https' --proto-redir '=https' --tlsv1.2 -fsS --max-time 5 "${BASE}/target" -o /dev/null 2>/dev/null; then
    fail "https-only flags fetched a plain-HTTP URL"
  else
    pass "https-only flags refuse a plain-HTTP URL"
  fi
fi

echo ""
echo "passed: ${PASSED}  failed: ${FAILED}"
[ "${FAILED}" -eq 0 ]
