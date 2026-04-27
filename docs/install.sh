#!/usr/bin/env bash
# Collabhost install script (Linux / macOS).
#
# Usage:
#   curl -fsSL https://mrbildo.github.io/collabhost/install.sh | bash
#   curl -fsSL https://mrbildo.github.io/collabhost/install.sh -o install.sh
#   bash install.sh [--version vX.Y.Z] [--install-path PATH] [--skip-path]
#
# Environment:
#   COLLABHOST_VERSION=vX.Y.Z  -- pin to a specific release (same as --version)
#
# Verifies archive SHA-256 against the release's checksums.txt before extracting.
# Preserves existing appsettings.json and data/ on re-run (upgrade-safe).
# Clears the macOS quarantine xattr on collabhost + caddy automatically.

set -euo pipefail

# ---- Defaults ----------------------------------------------------------------

REPO="MrBildo/collabhost"
INSTALL_PATH="${HOME}/.collabhost/bin"
SKIP_PATH=""
TAG="${COLLABHOST_VERSION:-}"

# ---- Arg parsing -------------------------------------------------------------

usage() {
  cat <<EOF
Collabhost installer (Linux / macOS)

Usage: install.sh [options]

Options:
  --version vX.Y.Z      Pin to a specific release tag (default: latest)
  --install-path PATH   Install to PATH (default: \$HOME/.collabhost/bin)
  --skip-path           Do not modify shell RC file for PATH
  --help                Print this message and exit

Environment:
  COLLABHOST_VERSION    Same as --version
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --version)
      TAG="${2:-}"
      shift 2
      ;;
    --install-path)
      INSTALL_PATH="${2:-}"
      shift 2
      ;;
    --skip-path)
      SKIP_PATH=1
      shift
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

# ---- Platform detection ------------------------------------------------------

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

# ---- Tool checks -------------------------------------------------------------

need() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required tool: $1" >&2
    exit 1
  }
}

need curl
need tar
need mktemp
need awk

# sha256sum OR shasum -- macOS ships shasum, Linux ships sha256sum.
if command -v sha256sum >/dev/null 2>&1; then
  SHA_CMD="sha256sum"
elif command -v shasum >/dev/null 2>&1; then
  SHA_CMD="shasum -a 256"
else
  echo "Missing required tool: sha256sum or shasum" >&2
  exit 1
fi

# ---- Resolve tag / version ---------------------------------------------------

if [ -z "${TAG}" ]; then
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

# Validate tag shape (vX.Y.Z). v1 does not support pre-release tags (decision 5).
if ! printf '%s' "${TAG}" | grep -Eq '^v[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "Invalid release tag '${TAG}' -- expected vX.Y.Z." >&2
  exit 1
fi

VERSION="${TAG#v}"
ARCHIVE="collabhost-${VERSION}-${RID}.${EXT}"
BASE_URL="https://github.com/${REPO}/releases/download/${TAG}"
ARCHIVE_URL="${BASE_URL}/${ARCHIVE}"
CHECKSUMS_URL="${BASE_URL}/checksums.txt"

# ---- Download + verify -------------------------------------------------------

TMP_DIR="$(mktemp -d)"
cleanup() { rm -rf "${TMP_DIR}"; }
trap cleanup EXIT

# Heartbeat: emit archive size from a HEAD request so the operator knows what
# to expect during the silent download window. The same HEAD response also
# serves as a pre-flight existence check -- a 404 here means the version tag
# does not exist on the release server (typo, deleted release, pre-release tag
# that passed the regex). Fatal on 404; non-fatal on all other failures so that
# a transient network error does not block the install.
SIZE_HINT=""
HEAD_HEADERS="$(curl -sIL --retry 3 --retry-delay 2 "${ARCHIVE_URL}" 2>/dev/null)" || true
HEAD_STATUS="$(printf '%s\n' "${HEAD_HEADERS}" | awk '/^HTTP\// {code=$2} END {print code+0}')" || true
if [ "${HEAD_STATUS}" = "404" ]; then
  echo "Release tag '${TAG}' not found. See https://github.com/${REPO}/releases for available versions." >&2
  exit 1
fi
RAW_CL="$(printf '%s\n' "${HEAD_HEADERS}" \
  | awk '/^[Cc]ontent-[Ll]ength:/ {print $2}' \
  | tr -d '\r' \
  | tail -1)" || true
if [ -n "${RAW_CL}" ] && [ "${RAW_CL}" -gt 0 ] 2>/dev/null; then
  SIZE_MB=$(( (RAW_CL + 524288) / 1048576 ))
  SIZE_HINT=" (~${SIZE_MB} MB)"
fi

echo "Downloading ${ARCHIVE}${SIZE_HINT}..."
curl -fsSL --retry 3 --retry-delay 2 "${ARCHIVE_URL}" -o "${TMP_DIR}/${ARCHIVE}"

echo "Downloading checksums.txt..."
curl -fsSL --retry 3 --retry-delay 2 "${CHECKSUMS_URL}" -o "${TMP_DIR}/checksums.txt"

echo "Verifying SHA-256..."
# Portable lookup -- match the archive name exactly in the second column.
# sha256sum output format: "<hash>  <filename>" (two spaces).
EXPECTED="$(awk -v name="${ARCHIVE}" '$2 == name {print $1; exit}' "${TMP_DIR}/checksums.txt")"
if [ -z "${EXPECTED}" ]; then
  echo "Could not find checksum for ${ARCHIVE} in checksums.txt" >&2
  exit 1
fi

ACTUAL="$(${SHA_CMD} "${TMP_DIR}/${ARCHIVE}" | awk '{print $1}')"
if [ "${EXPECTED}" != "${ACTUAL}" ]; then
  echo "Checksum mismatch for ${ARCHIVE}" >&2
  echo "  Expected: ${EXPECTED}" >&2
  echo "  Actual:   ${ACTUAL}" >&2
  exit 1
fi

# ---- Archive content pre-check ----------------------------------------------

# Guard against zero-byte or HTML-error downloads that somehow matched a
# checksum (defense in depth -- a valid checksum implies the bytes are what
# was published, but an operator who reruns after a partial download with
# a hand-edited checksums.txt would bypass that). Gzip magic is 0x1f 0x8b.
ARCHIVE_SIZE="$(wc -c < "${TMP_DIR}/${ARCHIVE}")"
if [ "${ARCHIVE_SIZE}" -lt 1024 ]; then
  echo "Archive ${ARCHIVE} looks truncated (${ARCHIVE_SIZE} bytes)." >&2
  exit 1
fi

MAGIC="$(head -c 2 "${TMP_DIR}/${ARCHIVE}" | od -An -tx1 | tr -d ' \n')"
if [ "${MAGIC}" != "1f8b" ]; then
  echo "Archive ${ARCHIVE} is not a valid gzip file (magic=${MAGIC})." >&2
  exit 1
fi

# ---- Extract -----------------------------------------------------------------

# The archive is flat -- the six contract items sit at the archive root, no
# wrapping directory. Extract straight into EXTRACT_DIR and copy from there.
EXTRACT_DIR="${TMP_DIR}/extract"
mkdir -p "${EXTRACT_DIR}"
echo "Extracting archive..."
tar -xzf "${TMP_DIR}/${ARCHIVE}" -C "${EXTRACT_DIR}"

if [ ! -f "${EXTRACT_DIR}/collabhost" ]; then
  echo "Archive layout unexpected: collabhost binary not found at archive root after extract." >&2
  exit 1
fi

# ---- Install (reinstall-safe) ------------------------------------------------

# Detect a pre-existing install BEFORE touching anything. Used to emit the
# "Preserved: ..." reassurance line on reinstalls.
IS_REINSTALL=""
if [ -f "${INSTALL_PATH}/appsettings.json" ] || [ -d "${INSTALL_PATH}/data" ]; then
  IS_REINSTALL=1
fi

mkdir -p "${INSTALL_PATH}"

# Overwrite files that are part of the bundle.
cp "${EXTRACT_DIR}/collabhost" "${INSTALL_PATH}/"
cp "${EXTRACT_DIR}/caddy"      "${INSTALL_PATH}/"
cp "${EXTRACT_DIR}/INSTALL.md" "${INSTALL_PATH}/"

mkdir -p "${INSTALL_PATH}/LICENSES"
# Clear then copy -- keeps LICENSES/ in sync with what the archive ships.
rm -f "${INSTALL_PATH}/LICENSES/"*
cp "${EXTRACT_DIR}/LICENSES/"* "${INSTALL_PATH}/LICENSES/"

# Preserve appsettings.json if it already exists. Only seed from the archive on
# first install. On upgrade the operator's edits survive (spec §9.7, R2.1).
if [ ! -f "${INSTALL_PATH}/appsettings.json" ]; then
  cp "${EXTRACT_DIR}/appsettings.json" "${INSTALL_PATH}/"
fi

if [ -n "${IS_REINSTALL}" ]; then
  DATA_HINT=""
  if [ -d "${INSTALL_PATH}/data" ]; then
    DATA_HINT=" and data/"
  fi
  echo "Preserved your existing appsettings.json${DATA_HINT}."
fi

# data/ is never in the archive -- leave any existing directory untouched.
# Merge-safe by construction.

chmod +x "${INSTALL_PATH}/collabhost" "${INSTALL_PATH}/caddy" || true

# ---- macOS: clear quarantine xattr (locked R2, §11.2) ------------------------

if [ "${UNAME_S}" = "Darwin" ]; then
  # || true -- xattr -d errors if the attribute is absent (e.g., on re-run).
  xattr -d com.apple.quarantine "${INSTALL_PATH}/collabhost" 2>/dev/null || true
  xattr -d com.apple.quarantine "${INSTALL_PATH}/caddy"      2>/dev/null || true
  echo "Cleared macOS quarantine attribute on collabhost and caddy."
fi

# ---- PATH integration --------------------------------------------------------

if [ -z "${SKIP_PATH}" ]; then
  case "${SHELL:-}" in
    */zsh)  RC_FILE="${HOME}/.zshrc"   ;;
    */bash) RC_FILE="${HOME}/.bashrc"  ;;
    *)      RC_FILE="${HOME}/.profile" ;;
  esac

  PATH_LINE="export PATH=\"${INSTALL_PATH}:\$PATH\""

  if ! grep -Fq "${PATH_LINE}" "${RC_FILE}" 2>/dev/null; then
    {
      echo ""
      echo "# Added by collabhost installer"
      echo "${PATH_LINE}"
    } >> "${RC_FILE}"
    echo "Added Collabhost to PATH in ${RC_FILE}"
    echo "Open a new terminal or run: source ${RC_FILE}"
  fi
fi

# ---- Summary -----------------------------------------------------------------

# Resolve bundled Caddy version for the Bundled: disclosure line. Non-fatal --
# if the binary won't execute (exec-bit, antivirus, exotic platform), fall back
# to omitting the version number.
CADDY_VERSION=""
CADDY_VERSION_RAW="$("${INSTALL_PATH}/caddy" version 2>/dev/null | awk '{print $1; exit}')" || true
if [ -n "${CADDY_VERSION_RAW}" ]; then
  CADDY_VERSION="${CADDY_VERSION_RAW}"
fi

BUNDLED_LINE="Bundled: collabhost ${TAG} + "
if [ -n "${CADDY_VERSION}" ]; then
  BUNDLED_LINE="${BUNDLED_LINE}Caddy ${CADDY_VERSION}"
else
  BUNDLED_LINE="${BUNDLED_LINE}Caddy (bundled)"
fi

echo ""
echo "Collabhost ${TAG} installed to ${INSTALL_PATH}"
echo "${BUNDLED_LINE}"
if [ -n "${IS_REINSTALL}" ]; then
  echo "Restart Collabhost to pick up the new binary. Your admin key and configuration are preserved."
else
  echo "Next: open a new terminal and run 'collabhost'. On first boot it prints your admin key -- copy it immediately."
fi
echo "See ${INSTALL_PATH}/INSTALL.md for configuration, env-var overrides, and upgrade notes."
