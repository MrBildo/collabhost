#!/usr/bin/env bash
# test-wipe-guard.sh -- assert assert_wipe_target accepts stage paths and refuses
# prod / system / malformed paths (card #443). Linux-only (uses GNU `realpath -m`,
# as on the box). Run: bash stage/tests/test-wipe-guard.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=../lib/stage-common.sh disable=SC1091
. "${HERE}/../lib/stage-common.sh"

# Anchors the guard reads (consumed inside the sourced assert_wipe_target).
# shellcheck disable=SC2034
{
  STAGE_DATA_ROOT=/var/lib/collabhost-stage
  STAGE_SRV=/srv/stage
  PROD_DATA=/var/lib/collabhost
  PROD_PREFIX=/opt/collabhost
  PROD_CONFIG_DIR=/etc/collabhost
}

PASS=0
FAIL=0

# A path that MUST be accepted (guard returns 0).
accept() {
  if ( assert_wipe_target "$1" ) >/dev/null 2>&1; then
    PASS=$((PASS + 1)); printf '  ok  accept  %s\n' "$1"
  else
    FAIL=$((FAIL + 1)); printf '  XX  accept  %s  (was REJECTED)\n' "$1"
  fi
}

# A path that MUST be refused (guard exits non-zero).
reject() {
  if ( assert_wipe_target "$1" ) >/dev/null 2>&1; then
    FAIL=$((FAIL + 1)); printf '  XX  reject  %s  (was ACCEPTED)\n' "$1"
  else
    PASS=$((PASS + 1)); printf '  ok  reject  %s\n' "$1"
  fi
}

echo "accept (legitimate stage wipe targets):"
accept /var/lib/collabhost-stage/data
accept /var/lib/collabhost-stage/app-data
accept /var/lib/collabhost-stage/caddy
accept /srv/stage
accept /srv/stage/demo-dotnet-api

echo "reject (prod / system / malformed):"
reject /var/lib/collabhost           # prod data root -- rejected at the stage-marker check (no 'collabhost-stage'/'/srv/stage' marker), before the prod-anchor block is reached
reject /var/lib/collabhost/data      # inside prod
reject /opt/collabhost               # prod prefix
reject /etc/collabhost               # prod config
reject /                             # root
reject /var/lib                      # shallow system
reject /srv                          # shallow system
reject /var/lib/collabhost-stage/../collabhost/data  # '..' traversal toward prod
reject /home/someone                 # no stage marker
reject foo/bar                       # not absolute
reject ""                            # empty

echo
echo "wipe-guard: ${PASS} passed, ${FAIL} failed"
[ "${FAIL}" -eq 0 ] || exit 1
