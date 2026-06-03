#!/usr/bin/env bash
# Collabhost install script (Linux / macOS) -- USER SCOPE.
#
# Installs Collabhost into the operator's home directory. No root required.
# For a system-scope install (/opt/collabhost + dedicated service user +
# /etc/systemd/system/collabhost.service), see install-system.sh.
#
# Usage:
#   curl -fsSL https://mrbildo.github.io/collabhost/install.sh | bash
#   curl -fsSL https://mrbildo.github.io/collabhost/install.sh -o install.sh
#   bash install.sh [--version vX.Y.Z] [--install-path PATH] [--skip-path]
#
# Environment:
#   COLLABHOST_VERSION=vX.Y.Z        -- pin to a specific release (same as --version)
#   COLLABHOST_INSTALL_BASE_URL=URL  -- override the archive download base URL (default: GitHub Releases)
#   COLLABHOST_INSTALL_SCRIPT_BASE_URL=URL  -- override the script/lib download base URL; the host this
#                                              script self-fetches install-lib.sh from when it is not
#                                              co-located (default: GitHub Pages)
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

# ---- Locate + source shared lib ----------------------------------------------

# The shared lib is sourced two ways:
#
#   1. Co-located: when install-lib.sh sits next to this script (a git clone, or
#      a deliberate two-file download), source it directly. This is also the path
#      CI exercises -- it runs the checked-out script from the repo where the lib
#      is co-located in docs/.
#
#   2. Self-fetched: the documented one-liners run this script standalone --
#      `curl ... install.sh -o install.sh; bash install.sh` or
#      `curl ... install.sh | bash`. Neither brings the lib along, and under a
#      pipe $0 is "bash" so there is no script directory to look in. In that
#      case, fetch the lib from the same host this script is published on and
#      source the downloaded copy. Operators never need to fetch a second file
#      by hand.
#
# COLLABHOST_INSTALL_SCRIPT_BASE_URL overrides the publish host (for a mirror or
# for testing). It is the script/lib host (GitHub Pages), distinct from
# COLLABHOST_INSTALL_BASE_URL, which overrides the archive host (GitHub Releases).
INSTALL_SCRIPT_BASE_URL="${COLLABHOST_INSTALL_SCRIPT_BASE_URL:-https://mrbildo.github.io/collabhost}"

source_install_lib() {
  # $0 is the script path when run as a file, or "bash" under a pipe. dirname of
  # "bash" is ".", so the co-located check simply misses under a pipe and we fall
  # through to the fetch -- exactly the behavior we want.
  local script_dir
  script_dir="$(cd "$(dirname "$0")" 2>/dev/null && pwd)" || script_dir=""

  if [ -n "${script_dir}" ] && [ -f "${script_dir}/install-lib.sh" ]; then
    # shellcheck source=install-lib.sh
    . "${script_dir}/install-lib.sh"
    return 0
  fi

  # Self-fetch. curl + mktemp are needed here, before require_common_tools runs.
  if ! command -v curl >/dev/null 2>&1; then
    echo "install.sh needs curl to fetch its shared library (install-lib.sh)." >&2
    echo "  Install curl and retry, or download both files side by side:" >&2
    echo "    curl -fsSL ${INSTALL_SCRIPT_BASE_URL}/install-lib.sh -o install-lib.sh" >&2
    echo "    curl -fsSL ${INSTALL_SCRIPT_BASE_URL}/install.sh -o install.sh" >&2
    exit 1
  fi
  if ! command -v mktemp >/dev/null 2>&1; then
    echo "install.sh needs mktemp to stage its shared library (install-lib.sh)." >&2
    exit 1
  fi

  local lib_url="${INSTALL_SCRIPT_BASE_URL}/install-lib.sh"
  local lib_tmp
  lib_tmp="$(mktemp)"
  echo "Fetching installer library from ${lib_url}..."
  if ! curl -fsSL --retry 3 --retry-delay 2 "${lib_url}" -o "${lib_tmp}"; then
    rm -f "${lib_tmp}"
    echo "Failed to fetch install-lib.sh from ${lib_url}." >&2
    echo "  Check your network, or set COLLABHOST_INSTALL_SCRIPT_BASE_URL to a reachable mirror." >&2
    exit 1
  fi

  # shellcheck source=install-lib.sh
  . "${lib_tmp}"
  rm -f "${lib_tmp}"
}

source_install_lib

# ---- Arg parsing -------------------------------------------------------------

usage() {
  cat <<EOF
Collabhost installer (Linux / macOS) -- user scope

Usage: install.sh [options]

Options:
  --version vX.Y.Z      Pin to a specific release tag (default: latest)
  --install-path PATH   Install to PATH (default: \$HOME/.collabhost/bin)
  --skip-path           Do not modify shell RC file for PATH
  --help                Print this message and exit

Environment:
  COLLABHOST_VERSION                  Same as --version
  COLLABHOST_INSTALL_BASE_URL         Override archive download base URL (default: GitHub Releases)
  COLLABHOST_INSTALL_SCRIPT_BASE_URL  Override script/lib download base URL -- the host this script
                                      self-fetches install-lib.sh from when not co-located (default: GitHub Pages)

For system-scope installs (root-owned /opt/collabhost layout + dedicated
service user + system-level systemd unit), see install-system.sh.
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

# ---- Platform + tools --------------------------------------------------------

detect_platform
require_common_tools
resolve_sha_command

# ---- Resolve tag / version ---------------------------------------------------

resolve_tag

ARCHIVE="collabhost-${VERSION}-${RID}.${EXT}"
BASE_URL="${COLLABHOST_INSTALL_BASE_URL:-https://github.com/${REPO}/releases/download/${TAG}}"
# ARCHIVE_URL / CHECKSUMS_URL are consumed by download_and_verify (sourced lib).
# shellcheck disable=SC2034
ARCHIVE_URL="${BASE_URL}/${ARCHIVE}"
# shellcheck disable=SC2034
CHECKSUMS_URL="${BASE_URL}/checksums.txt"

# ---- Download + verify -------------------------------------------------------

TMP_DIR="$(mktemp -d)"
cleanup() { rm -rf "${TMP_DIR}"; }
trap cleanup EXIT

download_and_verify

# ---- Extract -----------------------------------------------------------------

extract_archive

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

# wwwroot: always overwrite from the archive. This is the Portal SPA bundle
# and must track the binary version exactly. Operators do not edit it; new
# versions ship new bundles (see INSTALL.md §8 "Overwritten on re-run").
rm -rf "${INSTALL_PATH}/wwwroot"
cp -R "${EXTRACT_DIR}/wwwroot" "${INSTALL_PATH}/"

# wwwroot.sha256 sidecar: build-time SHA-256 hash of the wwwroot/ tree, written
# by the publish workflow. Sits next to the binary so the UAT runbook can
# compare against /api/v1/version.wwwrootHash (#342). Optional for archives
# predating #342 -- absence is silent.
if [ -f "${EXTRACT_DIR}/wwwroot.sha256" ]; then
  cp "${EXTRACT_DIR}/wwwroot.sha256" "${INSTALL_PATH}/"
fi

# appsettings.json: smart-merge on upgrade, plain copy on first install.
#
# First install: copy the archive's shipped appsettings.json into place AND seed the sidecar
# baseline (appsettings.shipped.json) so the next upgrade has a reference for distinguishing
# operator-edited keys from untouched defaults (card #161).
#
# Upgrade: invoke `collabhost --merge-appsettings <shipped> <ondisk> --baseline <baseline>` to
# perform the three-way merge. The new binary owns the merge logic so the same shape runs on
# every platform without duplicating JSON-handling code in PS + bash.
SHIPPED_SRC="${EXTRACT_DIR}/appsettings.json"
APPSETTINGS_DST="${INSTALL_PATH}/appsettings.json"
BASELINE_DST="${INSTALL_PATH}/appsettings.shipped.json"
COLLABHOST_BIN="${INSTALL_PATH}/collabhost"

smart_merge_appsettings "${SHIPPED_SRC}" "${APPSETTINGS_DST}" "${BASELINE_DST}" "${COLLABHOST_BIN}"

if [ -n "${IS_REINSTALL}" ]; then
  DATA_HINT=""
  if [ -d "${INSTALL_PATH}/data" ]; then
    DATA_HINT=" and data/"
  fi
  echo "Preserved your existing appsettings.json${DATA_HINT}."
fi

# Seed Proxy:BinaryPath in appsettings.json to the bundled caddy path on first
# install. On reinstall, leave the key alone -- if the operator pinned an
# external Caddy, we respect that. Smart-merge is intentionally minimal:
# absent or empty -> seed; present with a value -> leave alone.
#
# Prefer python3 (always present on macOS, near-universal on modern Linux).
# Fall back to awk against the known seed shape when python3 is unavailable.
BUNDLED_CADDY_PATH="${INSTALL_PATH}/caddy"
APPSETTINGS="${INSTALL_PATH}/appsettings.json"

seed_with_python() {
  python3 - "${APPSETTINGS}" "${BUNDLED_CADDY_PATH}" <<'PY'
import json
import sys

settings_path, bundled_path = sys.argv[1], sys.argv[2]
with open(settings_path, 'r', encoding='utf-8') as fh:
    data = json.load(fh)
proxy = data.get('Proxy')
if not isinstance(proxy, dict):
    proxy = {}
    data['Proxy'] = proxy
existing = proxy.get('BinaryPath')
if existing is None or (isinstance(existing, str) and existing.strip() == ''):
    proxy['BinaryPath'] = bundled_path
    with open(settings_path, 'w', encoding='utf-8') as fh:
        json.dump(data, fh, indent=2)
        fh.write('\n')
PY
}

seed_with_awk() {
  # Fallback: targets the known seed shape ("BinaryPath": "" inside a Proxy
  # block) and rewrites it in place. Only used when python3 is unavailable.
  # Bails (no rewrite) if the seed shape isn't present -- the operator will
  # have to set the key by hand or use COLLABHOST_CADDY_PATH.
  awk -v bundled="${BUNDLED_CADDY_PATH}" '
    BEGIN { in_proxy = 0; replaced = 0 }
    /"Proxy"[[:space:]]*:[[:space:]]*\{/ { in_proxy = 1; print; next }
    in_proxy && /"BinaryPath"[[:space:]]*:[[:space:]]*""/ {
      sub(/"BinaryPath"[[:space:]]*:[[:space:]]*""/, "\"BinaryPath\": \"" bundled "\"")
      replaced = 1
    }
    in_proxy && /\}/ { in_proxy = 0 }
    { print }
  ' "${APPSETTINGS}" > "${APPSETTINGS}.new" && mv "${APPSETTINGS}.new" "${APPSETTINGS}"
}

if command -v python3 >/dev/null 2>&1; then
  if ! seed_with_python; then
    echo "Warning: could not seed Proxy:BinaryPath in appsettings.json via python3." >&2
    echo "Set COLLABHOST_CADDY_PATH to '${BUNDLED_CADDY_PATH}' or repair the file by hand." >&2
  fi
else
  if ! seed_with_awk; then
    echo "Warning: could not seed Proxy:BinaryPath in appsettings.json via awk." >&2
    echo "Set COLLABHOST_CADDY_PATH to '${BUNDLED_CADDY_PATH}' or repair the file by hand." >&2
  fi
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
echo "After registering apps, run 'sudo collabhost --update-hosts' so <slug>.collab.internal resolves from this host. See INSTALL.md section 9.10.2."
echo "See ${INSTALL_PATH}/INSTALL.md for configuration, env-var overrides, and upgrade notes."
