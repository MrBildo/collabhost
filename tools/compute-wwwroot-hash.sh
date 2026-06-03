#!/usr/bin/env bash
# Deterministic content hash over a wwwroot/ tree -- the bash side of the
# Portal integrity contract (Card #342).
#
# This is the SINGLE bash implementation of the wwwroot hash. It is sourced by
# two consumers so the algorithm has exactly one copy and cannot drift between
# them:
#   1. .github/workflows/publish.yml "Compute wwwroot hash" step -- embeds the
#      digest into the published binary via -p:WwwrootHash.
#   2. The C#<->bash dual-compute seam test
#      (Collabhost.Api.Tests Portal/PortalIntegrityCheckTests.cs) -- shells this
#      script over a fixture tree and asserts equality with the C# routine
#      PortalIntegrityCheck.ComputeWwwrootHash over the same tree (Card #395).
#
# The algorithm MUST mirror PortalIntegrityCheck.ComputeWwwrootHash exactly:
#   1. Enumerate every file recursively under the root.
#   2. Relative POSIX paths (forward slashes, no leading ./), ordinal-sorted
#      (LC_ALL=C == byte order == C#'s StringComparer.Ordinal).
#   3. For each file: emit "<relativePath>\n<sizeBytes>\n<contentSha256Hex>\n".
#   4. Stream the concatenation through SHA-256; the lowercase hex digest is the
#      wwwroot hash.
# File metadata (mtime, owner, mode) is excluded by design -- tarball extraction
# rewrites mtime, which would make a metadata-inclusive hash unstable.
#
# Portability (the hash step runs on windows-latest, macos-latest, ubuntu in
# publish.yml -- BSD userland on macOS lacks GNU extensions):
#   - find -printf '%P\n' is GNU-only. Use `find . -type f` + strip leading './'.
#   - sha256sum is GNU coreutils; macOS only has `shasum -a 256`. Runtime-detect.
#   - stat -c is GNU; stat -f is BSD. Try GNU first, fall back to BSD.
#
# Usage: compute-wwwroot-hash.sh <wwwroot-dir>
# Prints the lowercase hex digest to stdout (no trailing newline beyond echo's).
set -euo pipefail

ROOT="${1:?usage: compute-wwwroot-hash.sh <wwwroot-dir>}"

if [[ ! -d "${ROOT}" ]]; then
  echo "wwwroot directory missing at ${ROOT}" >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  sha_cmd() { sha256sum "$1" | cut -d' ' -f1; }
  sha_stream() { sha256sum | cut -d' ' -f1; }
else
  sha_cmd() { shasum -a 256 "$1" | cut -d' ' -f1; }
  sha_stream() { shasum -a 256 | cut -d' ' -f1; }
fi

stat_size() { stat -c %s "$1" 2>/dev/null || stat -f %z "$1"; }

HASH=$(cd "${ROOT}" && find . -type f \
  | sed 's|^\./||' \
  | LC_ALL=C sort \
  | while IFS= read -r rel; do
      size=$(stat_size "${rel}")
      fhash=$(sha_cmd "${rel}")
      printf '%s\n%s\n%s\n' "${rel}" "${size}" "${fhash}"
    done \
  | sha_stream)

echo "${HASH}"
