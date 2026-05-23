#!/usr/bin/env bash
# Collabhost installer library -- shared helpers.
#
# RID, EXT, VERSION, EXTRACT_DIR, SHA_CMD, etc. are set here for the caller's
# benefit; shellcheck cannot see the caller's usage of sourced-script globals
# and would otherwise report SC2034 on each one. Disable at the file level.

# shellcheck disable=SC2034
#
# Sourced by install.sh (user-scope) and install-system.sh (system-scope). Not
# directly executable; requires the caller to set REPO and call functions in
# order.
#
# Convention: functions that need to communicate values to the caller assign to
# named globals (documented at the top of each function). Functions that only
# emit progress/diagnostic lines write to stdout/stderr directly. Every function
# returns 0 on success or exits non-zero on fatal failure -- callers do not need
# to inspect return codes for the fatal cases.
#
# Globals (caller pre-sets):
#   REPO                   -- "owner/repo" for download URL construction
#
# Globals set by detect_platform:
#   RID                    -- linux-x64 / linux-arm64 / osx-arm64
#   EXT                    -- archive extension (always tar.gz on POSIX)
#   UNAME_S                -- raw uname -s
#   UNAME_M                -- raw uname -m
#
# Globals set by resolve_sha_command:
#   SHA_CMD                -- "sha256sum" or "shasum -a 256"
#
# Globals set by resolve_tag (input: TAG may be pre-set; output: TAG, VERSION):
#   TAG                    -- vX.Y.Z release tag
#   VERSION                -- X.Y.Z (TAG with leading "v" stripped)
#
# Globals set by download_and_verify (inputs: TMP_DIR, ARCHIVE_URL, ARCHIVE,
#                                              CHECKSUMS_URL, SHA_CMD):
#   none -- writes "${TMP_DIR}/${ARCHIVE}" and "${TMP_DIR}/checksums.txt".
#
# Globals set by extract_archive (inputs: TMP_DIR, ARCHIVE; output: EXTRACT_DIR):
#   EXTRACT_DIR            -- directory holding the extracted archive contents
#
# Idempotency: every function below is safe to call once per invocation. Calling
# them twice is a caller bug, not a lib bug -- they may re-do work or emit
# duplicate diagnostics.

# ---- Platform detection ------------------------------------------------------

detect_platform() {
  UNAME_S="$(uname -s)"
  UNAME_M="$(uname -m)"

  case "${UNAME_S}-${UNAME_M}" in
    Linux-x86_64)   RID="linux-x64"   ; EXT="tar.gz" ;;
    Linux-aarch64)  RID="linux-arm64" ; EXT="tar.gz" ;;
    Linux-arm64)    RID="linux-arm64" ; EXT="tar.gz" ;;
    Darwin-arm64)   RID="osx-arm64"   ; EXT="tar.gz" ;;
    Darwin-x86_64)
      echo "Intel Mac (osx-x64) is not supported. Collabhost ships macOS builds for Apple Silicon (arm64) only." >&2
      echo "See https://github.com/${REPO}/releases for the osx-arm64 archive." >&2
      exit 1
      ;;
    *)
      echo "Unsupported platform: ${UNAME_S}-${UNAME_M}" >&2
      echo "Supported: Linux x86_64/aarch64, macOS arm64 (Apple Silicon)." >&2
      echo "Windows: use install.ps1." >&2
      exit 1
      ;;
  esac
}

# ---- Tool checks -------------------------------------------------------------

require_tool() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required tool: $1" >&2
    exit 1
  }
}

require_common_tools() {
  require_tool curl
  require_tool tar
  require_tool mktemp
  require_tool awk
}

resolve_sha_command() {
  # sha256sum OR shasum -- macOS ships shasum, Linux ships sha256sum.
  if command -v sha256sum >/dev/null 2>&1; then
    SHA_CMD="sha256sum"
  elif command -v shasum >/dev/null 2>&1; then
    SHA_CMD="shasum -a 256"
  else
    echo "Missing required tool: sha256sum or shasum" >&2
    exit 1
  fi
}

# ---- Tag resolution ----------------------------------------------------------

# Resolve the TAG global. If TAG is already set (operator passed --version /
# COLLABHOST_VERSION), validate its shape. Otherwise, query the GitHub releases
# API for the latest tag. Always sets VERSION to TAG with the leading "v"
# stripped on success.
resolve_tag() {
  if [ -z "${TAG:-}" ]; then
    echo "Resolving latest release from GitHub..."
    LATEST_JSON="$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest")" || {
      echo "Failed to query https://api.github.com/repos/${REPO}/releases/latest" >&2
      exit 1
    }
    # Parse tag_name without jq -- GitHub's JSON field is always on its own line
    # and always the string form "tag_name": "vX.Y.Z".
    TAG="$(printf '%s\n' "${LATEST_JSON}" \
      | awk -F'"' '/"tag_name"[[:space:]]*:/ {print $4; exit}')"
    if [ -z "${TAG}" ]; then
      echo "Could not parse latest tag from GitHub API response." >&2
      exit 1
    fi
  fi

  # Validate tag shape. Accepts vX.Y.Z and SemVer 2.0 §9 pre-release tags
  # (e.g. v1.2.1-rc1, v2.0.0-beta.3). Build metadata (+...) is intentionally
  # rejected -- archive filenames use VERSION as a path segment and '+' is
  # friction across tools. Keep this pattern in sync with publish.yml,
  # install-integration.yml, install.ps1, and install-system.ps1.
  if ! printf '%s' "${TAG}" | grep -Eq '^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$'; then
    echo "Invalid release tag '${TAG}' -- expected vX.Y.Z or vX.Y.Z-<pre-release>." >&2
    exit 1
  fi

  VERSION="${TAG#v}"
}

# ---- Download + verify -------------------------------------------------------

# Inputs: TMP_DIR, ARCHIVE, ARCHIVE_URL, CHECKSUMS_URL, SHA_CMD.
# Writes the archive + checksums.txt into TMP_DIR. Verifies the checksum and
# does a defense-in-depth gzip-magic check on the downloaded archive. Exits
# non-zero on any failure with an operator-readable message on stderr.
download_and_verify() {
  # Heartbeat: emit archive size from a HEAD request so the operator knows what
  # to expect during the silent download window. The same HEAD response also
  # serves as a pre-flight existence check -- a 404 here means the version tag
  # does not exist on the release server (typo, deleted release, pre-release tag
  # that passed the regex). Fatal on 404; non-fatal on all other failures so that
  # a transient network error does not block the install.
  local size_hint=""
  local head_headers=""
  local head_status=""
  local raw_cl=""
  local size_mb=""
  head_headers="$(curl -sIL --retry 3 --retry-delay 2 "${ARCHIVE_URL}" 2>/dev/null)" || true
  head_status="$(printf '%s\n' "${head_headers}" | awk '/^HTTP\// {code=$2} END {print code+0}')" || true
  if [ "${head_status}" = "404" ]; then
    echo "Release tag '${TAG}' not found. See https://github.com/${REPO}/releases for available versions." >&2
    exit 1
  fi
  raw_cl="$(printf '%s\n' "${head_headers}" \
    | awk '/^[Cc]ontent-[Ll]ength:/ {print $2}' \
    | tr -d '\r' \
    | tail -1)" || true
  if [ -n "${raw_cl}" ] && [ "${raw_cl}" -gt 0 ] 2>/dev/null; then
    size_mb=$(( (raw_cl + 524288) / 1048576 ))
    size_hint=" (~${size_mb} MB)"
  fi

  echo "Downloading ${ARCHIVE}${size_hint}..."
  curl -fsSL --retry 3 --retry-delay 2 "${ARCHIVE_URL}" -o "${TMP_DIR}/${ARCHIVE}"

  echo "Downloading checksums.txt..."
  curl -fsSL --retry 3 --retry-delay 2 "${CHECKSUMS_URL}" -o "${TMP_DIR}/checksums.txt"

  echo "Verifying SHA-256..."
  # Portable lookup -- match the archive name exactly in the second column.
  # sha256sum output format: "<hash>  <filename>" (two spaces).
  local expected
  local actual
  expected="$(awk -v name="${ARCHIVE}" '$2 == name {print $1; exit}' "${TMP_DIR}/checksums.txt")"
  if [ -z "${expected}" ]; then
    echo "Could not find checksum for ${ARCHIVE} in checksums.txt" >&2
    exit 1
  fi

  actual="$(${SHA_CMD} "${TMP_DIR}/${ARCHIVE}" | awk '{print $1}')"
  if [ "${expected}" != "${actual}" ]; then
    echo "Checksum mismatch for ${ARCHIVE}" >&2
    echo "  Expected: ${expected}" >&2
    echo "  Actual:   ${actual}" >&2
    exit 1
  fi

  # Guard against zero-byte or HTML-error downloads that somehow matched a
  # checksum (defense in depth -- a valid checksum implies the bytes are what
  # was published, but an operator who reruns after a partial download with
  # a hand-edited checksums.txt would bypass that). Gzip magic is 0x1f 0x8b.
  local archive_size
  archive_size="$(wc -c < "${TMP_DIR}/${ARCHIVE}")"
  if [ "${archive_size}" -lt 1024 ]; then
    echo "Archive ${ARCHIVE} looks truncated (${archive_size} bytes)." >&2
    exit 1
  fi

  local magic
  magic="$(head -c 2 "${TMP_DIR}/${ARCHIVE}" | od -An -tx1 | tr -d ' \n')"
  if [ "${magic}" != "1f8b" ]; then
    echo "Archive ${ARCHIVE} is not a valid gzip file (magic=${magic})." >&2
    exit 1
  fi
}

# ---- Extract -----------------------------------------------------------------

# Inputs: TMP_DIR, ARCHIVE.
# Output: EXTRACT_DIR (global).
# Extracts the archive into TMP_DIR/extract and verifies layout. The archive is
# flat -- the contract entries (collabhost, caddy, appsettings.json, INSTALL.md,
# LICENSES/, wwwroot/) sit at the archive root, no wrapping directory. See
# INSTALL.md section 4 for the operator-facing listing. Exits non-zero on layout
# surprise.
extract_archive() {
  EXTRACT_DIR="${TMP_DIR}/extract"
  mkdir -p "${EXTRACT_DIR}"
  echo "Extracting archive..."
  tar -xzf "${TMP_DIR}/${ARCHIVE}" -C "${EXTRACT_DIR}"

  if [ ! -f "${EXTRACT_DIR}/collabhost" ]; then
    echo "Archive layout unexpected: collabhost binary not found at archive root after extract." >&2
    exit 1
  fi

  if [ ! -f "${EXTRACT_DIR}/wwwroot/index.html" ]; then
    echo "Archive layout unexpected: wwwroot/index.html not found after extract." >&2
    exit 1
  fi
}

# ---- Smart-merge appsettings.json --------------------------------------------

# Inputs (positional): shipped_src, appsettings_dst, baseline_dst, collabhost_bin.
# Performs the same first-install seed / upgrade smart-merge as install.sh's
# inline logic. On first install (no appsettings_dst present), copies the
# shipped file to both appsettings_dst and baseline_dst. On upgrade, runs the
# new binary's --merge-appsettings subcommand if --version output matches the
# expected pattern; warns to stderr otherwise (recurrence-prevention from
# card #213).
#
# Caller is responsible for ensuring the parent directory of appsettings_dst
# exists and the binary is +x.
smart_merge_appsettings() {
  local shipped_src="$1"
  local appsettings_dst="$2"
  local baseline_dst="$3"
  local collabhost_bin="$4"

  if [ ! -f "${appsettings_dst}" ]; then
    cp "${shipped_src}" "${appsettings_dst}"
    cp "${shipped_src}" "${baseline_dst}"
    return 0
  fi

  chmod +x "${collabhost_bin}" 2>/dev/null || true
  local version_line
  version_line="$("${collabhost_bin}" --version 2>/dev/null || true)"

  # Match "Collabhost X.Y.Z" or "Collabhost vX.Y.Z" -- the optional 'v' guards against
  # any future change to VersionInfo.Current's prefix without re-breaking the gate
  # (card #213 root cause: the prior regex required 'v' that the binary doesn't emit).
  # Drop the major-version >= 1 constraint -- merge-appsettings shipped in v1.0.0,
  # so a v0.x binary won't recognize it and will exit non-zero, which the gate already
  # handles. Surface a warning when the regex misses so the next format drift is loud
  # instead of silent.
  local version_regex='^Collabhost v?[0-9]+\.[0-9]+\.[0-9]+'
  if echo "${version_line}" | grep -Eq "${version_regex}"; then
    if ! "${collabhost_bin}" --merge-appsettings "${shipped_src}" "${appsettings_dst}" --baseline "${baseline_dst}"; then
      echo "Warning: appsettings.json smart-merge failed."
      echo "Your existing appsettings.json was left in place; new shipped defaults may not be picked up automatically."
      echo "See ${appsettings_dst} and ${shipped_src} to reconcile by hand if needed."
    fi
  else
    echo "Warning: skipping appsettings.json smart-merge -- collabhost --version output did not match the expected pattern." >&2
    echo "  Got:      ${version_line:-<empty>}" >&2
    echo "  Expected: pattern '${version_regex}'" >&2
    echo "  Effect:   new shipped keys in appsettings.json may not be picked up automatically." >&2
    echo "  See ${appsettings_dst} and ${shipped_src} to reconcile by hand." >&2
  fi
}
