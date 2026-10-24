#!/usr/bin/env bash
# Prove which .NET runtime a self-contained Collabhost binary carries.
#
# A release is a self-contained publish, so the .NET runtime and the ASP.NET
# Core shared framework (Kestrel included) inside every archive are whatever
# the SDK that built it bundles. global.json pins that SDK. This script checks
# the chain end to end, so a release cannot silently ship an older runtime:
#
#   1. the SDK in use is exactly the version global.json pins;
#   2. that SDK reports the runtime version it bundles
#      (BundledNETCoreAppPackageVersion, read through MSBuild);
#   3. the Microsoft.NETCore.App and Microsoft.AspNetCore.App runtime packs
#      named in the deps manifest embedded in the single-file bundle must
#      both equal the version from step 2;
#   4. unless --no-exec is given, the binary is also run and the .NET host's
#      own trace must report the same runtime packs it loaded.
#
# .NET 10 servicing releases ship the runtime and ASP.NET Core at the same
# patch version, which is what steps 3 and 4 rely on. If a future release ever
# splits them, this check fails loudly rather than passing a mismatch.
#
# Usage: tools/verify-bundled-runtime.sh [--no-exec] <path-to-collabhost-binary>
# Use --no-exec where the binary cannot run on the build machine (the
# linux-arm64 release leg cross-compiles on an x64 runner; Collabhost is
# dynamically linked, so qemu alone cannot start it there).

set -euo pipefail

EXEC=1
if [[ "${1:-}" == "--no-exec" ]]; then
  EXEC=0
  shift
fi
BIN="${1:?usage: verify-bundled-runtime.sh [--no-exec] <path-to-collabhost-binary>}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/backend/Collabhost.Api/Collabhost.Api.csproj"

if [[ ! -f "${BIN}" ]]; then
  echo "Binary not found: ${BIN}" >&2
  exit 1
fi

# The first "version" key in global.json is sdk.version (msbuild-sdks entries
# are keyed by SDK name, not "version").
PINNED_SDK="$(awk -F'"' '/"version"/ { print $4; exit }' "${REPO_ROOT}/global.json")"
if [[ -z "${PINNED_SDK}" ]]; then
  echo "Could not read sdk.version from global.json" >&2
  exit 1
fi

# dotnet resolves global.json from the working directory; run it from the
# repo root so the pin is what selects the SDK. tr strips the CR a Windows
# dotnet.exe leaves on its output under Git Bash.
ACTIVE_SDK="$(cd "${REPO_ROOT}" && dotnet --version | tr -d '\r')"
BUNDLED_RUNTIME="$(cd "${REPO_ROOT}" && dotnet msbuild "${PROJECT}" -getProperty:BundledNETCoreAppPackageVersion | tr -d '\r[:space:]')"

echo "global.json SDK pin:      ${PINNED_SDK}"
echo "SDK in use:               ${ACTIVE_SDK}"
echo "Runtime that SDK bundles: ${BUNDLED_RUNTIME}"

if [[ "${ACTIVE_SDK}" != "${PINNED_SDK}" ]]; then
  echo "SDK mismatch: building with ${ACTIVE_SDK}, but global.json pins ${PINNED_SDK}." >&2
  exit 1
fi
if [[ ! "${BUNDLED_RUNTIME}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Could not read BundledNETCoreAppPackageVersion from the SDK (got '${BUNDLED_RUNTIME}')." >&2
  exit 1
fi

FAILED=0

# check_versions <source> <pack> <versions, one per line>
check_versions() {
  if [[ -z "$3" ]]; then
    echo "  MISSING (${1}): no ${2} runtime pack found." >&2
    FAILED=1
  elif [[ "$3" != "${BUNDLED_RUNTIME}" ]]; then
    echo "  MISMATCH (${1}): ${2} is '${3//$'\n'/, }', expected ${BUNDLED_RUNTIME}." >&2
    FAILED=1
  else
    echo "  ok (${1}): ${2} ${3}"
  fi
}

# Step 3: the single-file bundle stores its deps manifest uncompressed, and the
# manifest names each runtime pack as runtimepack.<pack>.Runtime.<rid>/<version>.
for pack in Microsoft.NETCore.App Microsoft.AspNetCore.App; do
  FOUND="$(LC_ALL=C grep -aoE "runtimepack\.${pack//./\\.}\.Runtime\.[A-Za-z0-9-]+/[0-9][0-9.]*" "${BIN}" \
    | sed 's#.*/##' | sort -u || true)"
  check_versions manifest "${pack}" "${FOUND}"
done

# Step 4: ask the binary itself.
if [[ "${EXEC}" -eq 1 ]]; then
  TRACE_FILE="$(mktemp)"
  trap 'rm -f "${TRACE_FILE}"' EXIT

  # The host writes its trace to stderr. --version exits before the app
  # starts, but only after the host has resolved the bundled runtime.
  if ! COREHOST_TRACE=1 "${BIN}" --version 2> "${TRACE_FILE}" > /dev/null; then
    echo "Running '${BIN} --version' failed." >&2
    tail -n 40 "${TRACE_FILE}" >&2
    exit 1
  fi

  for pack in Microsoft.NETCore.App Microsoft.AspNetCore.App; do
    # Lines look like:
    #   runtimepack: runtimepack.Microsoft.NETCore.App.Runtime.linux-x64, version: 10.0.13
    FOUND="$(tr -d '\r' < "${TRACE_FILE}" \
      | sed -n "s/.*runtimepack: runtimepack\.${pack//./\\.}\.Runtime\.[^,]*, version: \([0-9][0-9.]*\).*/\1/p" \
      | sort -u)"
    check_versions "host trace" "${pack}" "${FOUND}"
  done
else
  echo "  skipped (host trace): --no-exec, the binary cannot run on this machine."
fi

if [[ "${FAILED}" -ne 0 ]]; then
  echo "The binary does not carry the runtime the pinned SDK bundles." >&2
  exit 1
fi
echo "Bundled runtime verified: ${BUNDLED_RUNTIME} (SDK ${ACTIVE_SDK})."
