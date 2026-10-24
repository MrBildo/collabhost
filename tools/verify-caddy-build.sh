#!/usr/bin/env bash
# Check what a built Caddy binary records about how it was built, against the
# pins at the repo root:
#
#   1. it was compiled with the Go toolchain in go.version;
#   2. every override in caddy-replaces.txt landed in the binary, and is still
#      needed. A Go `replace` with a version on its right-hand side replaces
#      every version of the module, so an override that has outlived its
#      purpose does not show up on its own: once Caddy itself requires the
#      pinned version the line does nothing, and once Caddy requires a newer
#      version the line silently holds the module back. So the version the
#      build requires (the `dep` line) must be older than the override; equal
#      or newer fails, with an instruction to delete the line.
#
# It reads the build metadata with `go version -m`, so it never runs the binary
# and works for cross-compiled builds. Needs `go` on PATH.
#
# Versions are compared as major.minor.patch. Pre-release and pseudo-version
# suffixes are ignored, which is fine for the tagged releases this file holds.
#
# Usage: tools/verify-caddy-build.sh <path-to-caddy-binary>
# Shared by publish.yml, publish-dryrun.yml and tools/build-caddy.sh;
# tools/build-caddy.ps1 carries the same check in PowerShell.

set -euo pipefail

BIN="${1:?usage: verify-caddy-build.sh <path-to-caddy-binary>}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

BUILDINFO="$(go version -m "${BIN}" | tr -d '\r')"
EXPECTED_GO="go$(tr -d '[:space:]' < "${REPO_ROOT}/go.version")"
BUILT_GO="$(head -n 1 <<< "${BUILDINFO}" | awk '{print $NF}')"
if [[ "${BUILT_GO}" != "${EXPECTED_GO}" ]]; then
  echo "Caddy was compiled with ${BUILT_GO}, but go.version pins ${EXPECTED_GO}." >&2
  exit 1
fi
echo "  ok: compiled with ${BUILT_GO}"

FAILED=0
while IFS= read -r line || [[ -n "${line}" ]]; do
  trimmed="${line#"${line%%[![:space:]]*}"}"
  [[ -z "${trimmed}" || "${trimmed:0:1}" == "#" ]] && continue
  # shellcheck disable=SC2206
  parts=( ${trimmed} )
  if (( ${#parts[@]} != 2 )); then
    echo "Malformed replace line: ${line}" >&2
    exit 1
  fi
  module="${parts[0]}"
  version="${parts[1]}"

  # Prints the required version; exit 0 = live override, 1 = not in the
  # binary, 2 = stale (the build already requires the override or newer).
  if required="$(awk -v m="${module}" -v v="${version}" '
      function cmp(a, b,    x, y, i) {
        sub(/^v/, "", a); sub(/^v/, "", b)
        split(a, x, /[.+-]/); split(b, y, /[.+-]/)
        for (i = 1; i <= 3; i++) {
          if (x[i] + 0 < y[i] + 0) return -1
          if (x[i] + 0 > y[i] + 0) return 1
        }
        return 0
      }
      $1 == "dep" && $2 == m { dep = $3 }
      $1 == "=>" && $2 == m && $3 == v { found = 1 }
      END {
        print dep
        if (!found) exit 1
        if (cmp(dep, v) >= 0) exit 2
      }' <<< "${BUILDINFO}")"; then
    echo "  ok: ${module} ${required} => ${version}"
  else
    rc=$?
    if [[ "${rc}" -eq 2 ]]; then
      echo "  STALE override: ${module} ${version} (caddy-replaces.txt). The build already requires ${required}; delete this line." >&2
    else
      echo "  MISSING override: ${module} ${version} (caddy-replaces.txt) is not in the binary." >&2
    fi
    FAILED=1
  fi
done < "${REPO_ROOT}/caddy-replaces.txt"

if [[ "${FAILED}" -ne 0 ]]; then
  echo "--- go version -m ---" >&2
  echo "${BUILDINFO}" >&2
  exit 1
fi
