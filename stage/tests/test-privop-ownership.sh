#!/usr/bin/env bash
# test-privop-ownership.sh -- run stage-privop's install-artifacts against a scratch copy of the
# stage footprint and assert the privilege boundary stays root-owned (#474): /opt/collabhost-stage
# itself and everything under deploy/ (which holds stage-privop, run as root by stage-deploy) must
# never end up owned by, or writable for, the service user.
#
# Needs root and the collabhost-stage user, so run it on the box:
#   sudo bash stage/tests/test-privop-ownership.sh [path/to/stage-privop]
# With no argument it tests stage/box/stage-privop from this checkout. Pass an older copy to see
# the cases fail (the d23d7dc privop fails cases 1-7).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PRIVOP="${1:-${HERE}/../box/stage-privop}"
SVC=collabhost-stage

[ "$(id -u)" = "0" ] || { echo "run as root (sudo)" >&2; exit 2; }
id "$SVC" >/dev/null 2>&1 || { echo "needs the ${SVC} user (run on the stage box)" >&2; exit 2; }
[ -r "$PRIVOP" ] || { echo "no privop at ${PRIVOP}" >&2; exit 2; }

T="$(mktemp -d)"; trap 'rm -rf -- "$T"' EXIT; chmod 0755 "$T"

# Remap every hardcoded path into $T, then prove the remap is complete: each original path's
# occurrence count must equal the remapped count, so no step can reach a real stage path.
PATHS=(/opt/collabhost-stage /var/lib/collabhost-stage /srv/stage /home/stage-deploy/build /opt/collabhost/bin/caddy)
SED=()
for p in "${PATHS[@]}"; do SED+=( -e "s#${p}#${T}${p}#g" ); done
sed "${SED[@]}" "$PRIVOP" > "$T/privop"
for p in "${PATHS[@]}"; do
  want="$(grep -o -- "$p" "$PRIVOP" | wc -l)"; got="$(grep -o -- "${T}${p}" "$T/privop" | wc -l)"
  [ "$want" = "$got" ] || { echo "remap incomplete for ${p}: ${want} vs ${got}" >&2; exit 2; }
done
# Closure: no absolute path under a writable root may survive un-remapped, so a path added to
# the helper later (and missing from PATHS) stops the test instead of running on the real box.
left="$(sed '/^[[:space:]]*#/d' "$T/privop" | grep -oE '(^|[^A-Za-z0-9._/-])/(opt|var|srv|home|etc)(/[A-Za-z0-9._-]*)*' || true)"
[ -z "$left" ] || { echo "un-remapped paths left in the helper copy:" >&2; echo "$left" >&2; exit 2; }
PREFIX="${T}/opt/collabhost-stage"

setup() {
  rm -rf -- "${T:?}/opt" "${T:?}/home" "${T:?}/var" "${T:?}/srv"
  install -d -m 0755 -o root -g root "$PREFIX" "${PREFIX}/deploy" "${T}/opt/collabhost/bin"
  install -m 0644 -o root -g root /dev/null "${PREFIX}/deploy/marker"
  install -d -m 0755 "${T}/home/stage-deploy/build/publish/wwwroot"
  printf '#!/bin/sh\n' > "${T}/home/stage-deploy/build/publish/collabhost"; chmod 0755 "${T}/home/stage-deploy/build/publish/collabhost"
  echo ui > "${T}/home/stage-deploy/build/publish/wwwroot/index.html"
  printf '#!/bin/sh\n' > "${T}/opt/collabhost/bin/caddy"; chmod 0755 "${T}/opt/collabhost/bin/caddy"
}

PASS=0; FAIL=0
check() { if [ "$2" = "$3" ]; then PASS=$((PASS+1)); printf '  ok  %s\n' "$1"; else FAIL=$((FAIL+1)); printf '  XX  %s (want %s, got %s)\n' "$1" "$2" "$3"; fi; }
owner() { stat -c %U "$1"; }

echo "case 1: healthy footprint -> install-artifacts succeeds, boundary stays root"
setup
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 0 "$rc"
check "prefix owner" root "$(owner "$PREFIX")"
check "deploy/ tree all root, none group/world-writable" "" "$(find "${PREFIX}/deploy" \( ! -user root -o -perm /022 \) -print)"
check "bin/collabhost owner" "$SVC" "$(owner "${PREFIX}/bin/collabhost")"
check "bin/caddy owner" "$SVC" "$(owner "${PREFIX}/bin/caddy")"
check "wwwroot/index.html owner" "$SVC" "$(owner "${PREFIX}/wwwroot/index.html")"

echo "case 2: deploy/ already owned by the service user -> refused (exit 8)"
setup; chown -R "${SVC}:${SVC}" "${PREFIX}/deploy"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "case 3: a group-writable file in deploy/ -> refused (exit 8)"
setup; chmod 0664 "${PREFIX}/deploy/marker"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "case 4: the prefix itself owned by the service user -> refused (exit 8)"
setup; chown "${SVC}:${SVC}" "$PREFIX"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "case 5: the prefix group-writable -> refused (exit 8)"
setup; chmod 0775 "$PREFIX"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "case 6: deploy/ missing -> refused, fails closed (exit 8)"
setup; rm -rf -- "${PREFIX:?}/deploy"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "case 7: a symlink inside deploy/ -> refused (exit 8)"
setup; ln -s /tmp "${PREFIX}/deploy/link"
rc=0; bash "$T/privop" install-artifacts >/dev/null 2>&1 || rc=$?
check "exit code" 8 "$rc"

echo "passed ${PASS}, failed ${FAIL}"
[ "$FAIL" -eq 0 ]
