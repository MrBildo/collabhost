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

REPO="mrbildo/collabhost"
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
  Darwin-x86_64)  RID="osx-x64"     ; EXT="tar.gz" ;;
  Darwin-arm64)   RID="osx-arm64"   ; EXT="tar.gz" ;;
  *)
    echo "Unsupported platform: ${UNAME_S}-${UNAME_M}" >&2
    echo "Supported: Linux x86_64/aarch64, macOS x86_64/arm64." >&2
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

echo "Downloading ${ARCHIVE}..."
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

# ---- Extract -----------------------------------------------------------------

EXTRACT_DIR="${TMP_DIR}/extract"
mkdir -p "${EXTRACT_DIR}"
tar -xzf "${TMP_DIR}/${ARCHIVE}" -C "${EXTRACT_DIR}"

ARCHIVE_ROOT="${EXTRACT_DIR}/collabhost-${VERSION}-${RID}"
if [ ! -d "${ARCHIVE_ROOT}" ]; then
  echo "Archive layout unexpected: ${ARCHIVE_ROOT} not found after extract." >&2
  exit 1
fi

# ---- Install (reinstall-safe) ------------------------------------------------

mkdir -p "${INSTALL_PATH}"

# Overwrite files that are part of the bundle.
cp "${ARCHIVE_ROOT}/collabhost" "${INSTALL_PATH}/"
cp "${ARCHIVE_ROOT}/caddy"      "${INSTALL_PATH}/"
cp "${ARCHIVE_ROOT}/INSTALL.md" "${INSTALL_PATH}/"

mkdir -p "${INSTALL_PATH}/LICENSES"
# Clear then copy -- keeps LICENSES/ in sync with what the archive ships.
rm -f "${INSTALL_PATH}/LICENSES/"*
cp "${ARCHIVE_ROOT}/LICENSES/"* "${INSTALL_PATH}/LICENSES/"

# Preserve appsettings.json if it already exists. Only seed from the archive on
# first install. On upgrade the operator's edits survive (spec §9.7, R2.1).
if [ ! -f "${INSTALL_PATH}/appsettings.json" ]; then
  cp "${ARCHIVE_ROOT}/appsettings.json" "${INSTALL_PATH}/"
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

echo ""
echo "Collabhost ${TAG} installed to ${INSTALL_PATH}"
echo "Admin key: run 'collabhost' once to generate; first-run stdout captures it."
echo "See ${INSTALL_PATH}/INSTALL.md for configuration, env-var overrides, and upgrade notes."
