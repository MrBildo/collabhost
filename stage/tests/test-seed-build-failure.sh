#!/usr/bin/env bash
# test-seed-build-failure.sh -- assert that ONE demo's build failure does NOT fail
# the whole seed (card #443 hardening). A failing demo build must be non-fatal:
# warn, skip that demo (it is never registered -- an un-built artifact would just
# 502), continue with the rest, and report which were skipped. This is the same
# warn-skip-continue an ABSENT toolchain already gets; a build that runs and fails
# was inconsistently fatal (one bad `dotnet publish` `set -e`'d an otherwise-green
# deploy with a non-zero exit). Linux-only (bash + python3 + coreutils, as on the
# box). Run: bash stage/tests/test-seed-build-failure.sh
#
# Self-contained: the seed's two external surfaces are stubbed on PATH -- a `curl`
# that emulates the three control-plane calls (list / register / start) and a `sudo`
# passthrough so `privop seed-install` runs the no-op STAGE_PRIVOP without privilege.
# No network, no sudo, no real control plane.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SEED="${HERE}/../seed-demo-apps.sh"
[ -f "${SEED}" ] || { echo "seed script not found: ${SEED}" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# --- the curated test set: a good build, a FAILING build, and a no-build app -----
# Ordered so the failing build is reached before a later good one -- proving the
# seed continues past the failure rather than aborting the whole run.
DEMO_DIR="${WORK}/demo-apps"
mkdir -p "${DEMO_DIR}/demo-good" "${DEMO_DIR}/demo-bad" "${DEMO_DIR}/demo-keep"

cat > "${DEMO_DIR}/manifest.json" <<'JSON'
{
  "apps": [
    { "slug": "demo-good", "displayName": "Good", "appType": "static-site",
      "artifactSource": "demo-good", "build": "true",  "requires": "", "start": false,
      "values": {} },
    { "slug": "demo-bad",  "displayName": "Bad",  "appType": "static-site",
      "artifactSource": "demo-bad",  "build": "false", "requires": "", "start": false,
      "values": {} },
    { "slug": "demo-keep", "displayName": "Keep", "appType": "static-site",
      "artifactSource": "demo-keep", "build": "",      "requires": "", "start": false,
      "values": {} }
  ]
}
JSON

# --- told inputs (instance.env) --------------------------------------------------
KEY_FILE="${WORK}/admin-key"
printf 'test-admin-key' > "${KEY_FILE}"

INSTANCE_ENV="${WORK}/instance.env"
cat > "${INSTANCE_ENV}" <<EOF
STAGE_BASE_URL=http://127.0.0.1:0
STAGE_ADMIN_KEY_FILE=${KEY_FILE}
STAGE_SRV=/srv/stage
EOF

# --- stub PATH: curl (canned control plane) + sudo (passthrough) -----------------
STUB_BIN="${WORK}/bin"
mkdir -p "${STUB_BIN}"

REG_LOG="${WORK}/registered.txt"
: > "${REG_LOG}"

cat > "${STUB_BIN}/curl" <<'CURL'
#!/usr/bin/env bash
# Emulates ONLY the three calls seed-demo-apps.sh makes:
#   GET  .../api/v1/apps           -> empty app list JSON (everything is absent)
#   POST .../api/v1/apps           -> 201 + log the registered slug
#   POST .../api/v1/apps/<s>/start -> 204
set -u
out=""; method="GET"; data=""; url=""; prev=""
for a in "$@"; do
  case "$prev" in
    -o)            out="$a";    prev=""; continue ;;
    -X)            method="$a"; prev=""; continue ;;
    --data-binary) data="$a";   prev=""; continue ;;
    -H|-w)                      prev=""; continue ;;
  esac
  case "$a" in
    -o|-X|--data-binary|-H|-w) prev="$a" ;;
    http://*|https://*)        url="$a" ;;
    *) : ;;
  esac
done
case "$url" in
  */start)
    [ -n "$out" ] && : > "$out"
    printf '204' ;;
  */api/v1/apps)
    if [ "$method" = "POST" ]; then
      slug="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["name"])' "${data#@}" 2>/dev/null || true)"
      [ -n "$slug" ] && printf '%s\n' "$slug" >> "$CURL_REG_LOG"
      [ -n "$out" ] && printf '{}' > "$out"
      printf '201'
    else
      printf '{"items":[]}'
    fi ;;
  *) printf '000' ;;
esac
CURL
chmod +x "${STUB_BIN}/curl"

cat > "${STUB_BIN}/sudo" <<'SUDO'
#!/usr/bin/env bash
# Passthrough: drop privilege escalation, run the command directly. STAGE_PRIVOP is
# /bin/true in this test, so `sudo /bin/true seed-install` is a clean no-op.
exec "$@"
SUDO
chmod +x "${STUB_BIN}/sudo"

# --- run the real seed against the stubs -----------------------------------------
set +e
OUT="$(
  PATH="${STUB_BIN}:${PATH}" \
  CURL_REG_LOG="${REG_LOG}" \
  COLLABHOST_STAGE_INSTANCE_ENV="${INSTANCE_ENV}" \
  STAGE_DEMO_APPS_DIR="${DEMO_DIR}" \
  STAGE_PRIVOP="/bin/true" \
    bash "${SEED}" 2>&1
)"
RC=$?
set -e

echo "----- seed output -----"
echo "${OUT}"
echo "----- seed exit: ${RC} -----"
echo

PASS=0
FAIL=0
pass() { PASS=$((PASS + 1)); printf '  ok  %s\n' "$1"; }
fail() { FAIL=$((FAIL + 1)); printf '  XX  %s\n' "$1"; }

# 1. The whole seed succeeded despite demo-bad's build failing.
if [ "${RC}" -eq 0 ]; then
  pass "seed exits 0 (one bad build did not fail the deploy)"
else
  fail "seed exits 0 (got ${RC} -- a bad build aborted the deploy)"
fi

# 2. The failing demo was warned about and skipped.
if printf '%s\n' "${OUT}" | grep -q 'skip demo-bad: build failed'; then
  pass "demo-bad reported skipped (build failed)"
else
  fail "demo-bad reported skipped (build failed)"
fi

# 3. The summary reports the build-skip count.
if printf '%s\n' "${OUT}" | grep -q 'skipped-build=1'; then
  pass "summary reports skipped-build=1"
else
  fail "summary reports skipped-build=1"
fi

# 4 + 5. The good and no-build apps still registered (the seed continued).
if grep -qxF demo-good "${REG_LOG}"; then
  pass "demo-good registered (seed continued past the failure)"
else
  fail "demo-good registered (seed continued past the failure)"
fi
if grep -qxF demo-keep "${REG_LOG}"; then
  pass "demo-keep registered (no-build app unaffected)"
else
  fail "demo-keep registered (no-build app unaffected)"
fi

# 6. The failed-build app was NOT registered (an un-built artifact would 502).
if grep -qxF demo-bad "${REG_LOG}"; then
  fail "demo-bad NOT registered (it was registered despite never building)"
else
  pass "demo-bad NOT registered (it never built)"
fi

echo
echo "seed-build-failure: ${PASS} passed, ${FAIL} failed"
[ "${FAIL}" -eq 0 ] || exit 1
