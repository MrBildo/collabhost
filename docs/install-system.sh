#!/usr/bin/env bash
# Collabhost install script (Linux) -- SYSTEM SCOPE.
#
# Lays the canonical Linux server layout under root-owned paths and installs a
# system-level systemd unit running as a dedicated `collabhost` system user.
#
#   /opt/collabhost/bin/collabhost      -- binary
#   /opt/collabhost/bin/caddy           -- bundled Caddy
#   /opt/collabhost/wwwroot/            -- frontend (Portal SPA)
#   /opt/collabhost/INSTALL.md          -- operator docs
#   /opt/collabhost/LICENSES/           -- bundled-binary license texts
#   /etc/collabhost/appsettings.json    -- operator-facing config
#   /etc/collabhost/appsettings.shipped.json  -- smart-merge baseline (do not edit)
#   /var/lib/collabhost/data/           -- SQLite DB + backups
#   /var/lib/collabhost/user-types/     -- operator-authored AppType JSON
#   /var/lib/collabhost/caddy/          -- Caddy CA / cert storage
#   /var/log/collabhost/                -- crash logs
#   /etc/systemd/system/collabhost.service  -- system-scope unit
#
# Requires root. The script re-execs under sudo if it can find one and is not
# already running as root; otherwise it exits with a clear message.
#
# Usage:
#   sudo bash install-system.sh [--version vX.Y.Z]
#   curl -fsSL https://mrbildo.github.io/collabhost/install-system.sh | sudo bash
#
# Environment:
#   COLLABHOST_VERSION=vX.Y.Z        -- pin to a specific release (same as --version)
#   COLLABHOST_INSTALL_BASE_URL=URL  -- override the archive download base URL
#
# Re-runnable. On re-run, binaries / wwwroot / unit / LICENSES are overwritten
# with the new release's contents; appsettings.json is smart-merged; data and
# Caddy storage are left untouched.
#
# Linux only. macOS does not have systemd; for a per-user install on macOS, use
# install.sh.
#
# Card #230 phase 3. Closes #214 by construction (system unit's
# AmbientCapabilities resolves the cap_net_bind_service-on-cp-upgrade issue).

set -euo pipefail

# ---- Defaults ----------------------------------------------------------------

REPO="MrBildo/collabhost"
TAG="${COLLABHOST_VERSION:-}"

# Canonical layout. Hardcoded for v1 -- the whole point of this script is the
# canonical layout. If you need a custom layout, use install.sh + a hand-rolled
# unit, or copy this script and edit the constants.
SERVICE_USER="collabhost"
SERVICE_GROUP="collabhost"
INSTALL_PREFIX="/opt/collabhost"
BIN_DIR="${INSTALL_PREFIX}/bin"
CONFIG_DIR="/etc/collabhost"
DATA_ROOT="/var/lib/collabhost"
DATA_DIR="${DATA_ROOT}/data"
USER_TYPES_DIR="${DATA_ROOT}/user-types"
CADDY_STORAGE_DIR="${DATA_ROOT}/caddy"
# Single-file .NET host extraction target. Under DATA_ROOT so the existing
# ReadWritePaths covers it and teardown's `rm -rf /var/lib/collabhost` reaps it.
DOTNET_BUNDLE_DIR="${DATA_ROOT}/dotnet-bundle"
LOG_DIR="/var/log/collabhost"
UNIT_DIR="/etc/systemd/system"
UNIT_NAME="collabhost.service"
UNIT_PATH="${UNIT_DIR}/${UNIT_NAME}"

# ---- Locate + source shared lib ----------------------------------------------

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=_install-lib.sh
. "${SCRIPT_DIR}/_install-lib.sh"

# ---- Arg parsing -------------------------------------------------------------

usage() {
  cat <<EOF
Collabhost installer (Linux) -- SYSTEM scope

Lays /opt/collabhost + /etc/collabhost + /var/lib/collabhost + a system-scope
systemd unit running under a dedicated 'collabhost' system user. Requires root.

Usage: install-system.sh [options]

Options:
  --version vX.Y.Z   Pin to a specific release tag (default: latest)
  --help             Print this message and exit

Environment:
  COLLABHOST_VERSION           Same as --version
  COLLABHOST_INSTALL_BASE_URL  Override archive download base URL (default: GitHub Releases)

For a user-scope install in \$HOME (no root required), see install.sh.
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --version)
      TAG="${2:-}"
      shift 2
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

# ---- Linux-only gate ---------------------------------------------------------

# Detect platform early so we can reject non-Linux before doing anything else.
detect_platform

if [ "${UNAME_S}" != "Linux" ]; then
  echo "install-system.sh is Linux-only (no systemd elsewhere)." >&2
  echo "  Detected: ${UNAME_S} ${UNAME_M}" >&2
  echo "  For per-user installs on macOS, use install.sh." >&2
  echo "  For Windows, use install.ps1 (system-service support is a separate card)." >&2
  exit 1
fi

# ---- Root + sudo posture -----------------------------------------------------

# Hard-fail if not root and sudo is unavailable. Re-exec under sudo otherwise.
# The re-exec preserves env vars the operator might have set (COLLABHOST_VERSION,
# COLLABHOST_INSTALL_BASE_URL) via sudo's -E and explicit forwarding.
require_root() {
  if [ "$(id -u)" -eq 0 ]; then
    return 0
  fi

  if ! command -v sudo >/dev/null 2>&1; then
    echo "install-system.sh requires root." >&2
    echo "  Re-run as root, or install sudo and retry." >&2
    exit 1
  fi

  echo "Re-executing under sudo (system-scope install requires root)..."
  # -H normalizes HOME for the elevated process; -E is intentionally avoided so
  # the operator's full env doesn't leak into the install -- we forward only the
  # COLLABHOST_* env vars the script reads.
  #
  # COLLABHOST_VERSION fallback to ${TAG} preserves --version: arg parsing
  # already ran, so $TAG holds the operator's choice but $@ is empty and $TAG
  # is a shell var (not exported). Forwarding it via the env-var slot lets the
  # re-exec'd script's resolve_tag pick it up without round-tripping through
  # CLI argv. (Bug #242.)
  exec sudo -H \
    COLLABHOST_VERSION="${COLLABHOST_VERSION:-${TAG:-}}" \
    COLLABHOST_INSTALL_BASE_URL="${COLLABHOST_INSTALL_BASE_URL:-}" \
    bash "$0" "$@"
}

require_root "$@"

# ---- Tools + tag resolution --------------------------------------------------

require_common_tools
require_tool useradd
require_tool getent
require_tool install
require_tool systemctl
resolve_sha_command
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
extract_archive

# ---- Service user ------------------------------------------------------------

# Idempotent: useradd -r returns non-zero if the user already exists. Check
# first via getent so we don't paper over real failures.
if getent passwd "${SERVICE_USER}" >/dev/null 2>&1; then
  echo "Service user '${SERVICE_USER}' already exists -- reusing."
else
  echo "Creating service user '${SERVICE_USER}'..."
  # --system: UID below the system threshold (no login).
  # --no-create-home: home directory is unused (data lives under /var/lib).
  # --shell /usr/sbin/nologin: belt-and-suspenders against interactive login.
  # --user-group: matching group with the same name (Debian/Ubuntu convention;
  #   Red-Hat-family useradd does this by default but the flag is harmless).
  useradd \
    --system \
    --no-create-home \
    --shell /usr/sbin/nologin \
    --user-group \
    --comment "Collabhost service user" \
    "${SERVICE_USER}"
fi

# ---- Layout ------------------------------------------------------------------

# Detect a pre-existing install BEFORE touching anything. Used to emit the
# "Preserved: ..." reassurance line on reinstalls.
IS_REINSTALL=""
if [ -f "${CONFIG_DIR}/appsettings.json" ] || [ -d "${DATA_DIR}" ]; then
  IS_REINSTALL=1
fi

# install -d creates the directory with the requested mode/owner in one call;
# safer than mkdir+chown+chmod because there's no window between create and
# chown where the directory exists with the wrong owner.
install -d -m 0755 -o root -g root                               "${INSTALL_PREFIX}"
install -d -m 0755 -o root -g root                               "${BIN_DIR}"
install -d -m 0755 -o root -g root                               "${CONFIG_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${DATA_ROOT}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${DATA_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${USER_TYPES_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${CADDY_STORAGE_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${DOTNET_BUNDLE_DIR}"
install -d -m 0750 -o "${SERVICE_USER}" -g "${SERVICE_GROUP}"    "${LOG_DIR}"

# ---- Install bundle artifacts ------------------------------------------------

# Binaries (root-owned, world-readable+executable).
install -m 0755 -o root -g root "${EXTRACT_DIR}/collabhost" "${BIN_DIR}/collabhost"
install -m 0755 -o root -g root "${EXTRACT_DIR}/caddy"      "${BIN_DIR}/caddy"

# Documentation (root-owned, world-readable).
install -m 0644 -o root -g root "${EXTRACT_DIR}/INSTALL.md" "${INSTALL_PREFIX}/INSTALL.md"

# LICENSES dir (clear + repopulate to stay in sync with archive).
install -d -m 0755 -o root -g root "${INSTALL_PREFIX}/LICENSES"
rm -f "${INSTALL_PREFIX}/LICENSES/"*
for license_file in "${EXTRACT_DIR}/LICENSES/"*; do
  install -m 0644 -o root -g root "${license_file}" "${INSTALL_PREFIX}/LICENSES/"
done

# wwwroot: always overwrite from the archive (Portal SPA, must track binary).
rm -rf "${INSTALL_PREFIX}/wwwroot"
cp -R "${EXTRACT_DIR}/wwwroot" "${INSTALL_PREFIX}/wwwroot"
chown -R root:root "${INSTALL_PREFIX}/wwwroot"
chmod -R u=rwX,go=rX "${INSTALL_PREFIX}/wwwroot"

# ---- Config ------------------------------------------------------------------

# appsettings.json + appsettings.shipped.json live in /etc/collabhost. Smart-merge
# uses the same library helper as install.sh so the two scripts behave identically
# on upgrade.
SHIPPED_SRC="${EXTRACT_DIR}/appsettings.json"
APPSETTINGS_DST="${CONFIG_DIR}/appsettings.json"
BASELINE_DST="${CONFIG_DIR}/appsettings.shipped.json"
COLLABHOST_BIN="${BIN_DIR}/collabhost"

smart_merge_appsettings "${SHIPPED_SRC}" "${APPSETTINGS_DST}" "${BASELINE_DST}" "${COLLABHOST_BIN}"

# Make /etc/collabhost contents readable by the service user but not world. The
# admin key may live in appsettings.json (Auth:AdminKey) when the operator chose
# that shape, so 0640 (root:collabhost) is the right floor.
chown root:"${SERVICE_GROUP}" "${APPSETTINGS_DST}"
chmod 0640                    "${APPSETTINGS_DST}"
if [ -f "${BASELINE_DST}" ]; then
  chown root:"${SERVICE_GROUP}" "${BASELINE_DST}"
  chmod 0640                    "${BASELINE_DST}"
fi

# Seed Proxy:BinaryPath in appsettings.json to the bundled caddy path on first
# install. Same shape as install.sh; the bundled caddy is at /opt/collabhost/bin/caddy.
# Only python3 path -- system installs are Linux-only and python3 is effectively
# universal on modern server distros. Fall back to a manual-recovery message if
# python3 is somehow absent or the script fails.
BUNDLED_CADDY_PATH="${BIN_DIR}/caddy"

seed_proxy_binary_path() {
  python3 - "${APPSETTINGS_DST}" "${BUNDLED_CADDY_PATH}" <<'PY'
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

if command -v python3 >/dev/null 2>&1; then
  if ! seed_proxy_binary_path; then
    echo "Warning: could not seed Proxy:BinaryPath in appsettings.json via python3." >&2
    echo "Edit ${APPSETTINGS_DST} and set Proxy.BinaryPath to '${BUNDLED_CADDY_PATH}'." >&2
  fi
else
  echo "Warning: python3 not found -- Proxy:BinaryPath was not seeded." >&2
  echo "Edit ${APPSETTINGS_DST} and set Proxy.BinaryPath to '${BUNDLED_CADDY_PATH}'." >&2
fi

# Re-apply ownership/perms after the python3 rewrite (atomic-replace patterns
# don't always preserve ownership on edited files).
chown root:"${SERVICE_GROUP}" "${APPSETTINGS_DST}"
chmod 0640                    "${APPSETTINGS_DST}"

# ---- systemd unit ------------------------------------------------------------

# The bundled unit lives in the repo under systemd/collabhost.system.service.
# It is NOT in the archive (the archive contract is locked at 7 items via
# publish.yml). We embed it here -- keep this in lockstep with
# systemd/collabhost.system.service in the repo. The two should be byte-for-byte
# identical apart from the absence of leading commentary that documents the
# repo file's purpose. (Drift detection: a CI lint could compare the embedded
# heredoc against the repo file. Filed as a follow-up if it becomes a real
# problem.)
echo "Installing systemd unit at ${UNIT_PATH}..."
install -d -m 0755 -o root -g root "${UNIT_DIR}"

cat > "${UNIT_PATH}" <<UNIT
# Collabhost -- systemd system-scope unit (production).
#
# Installed by install-system.sh ${TAG}. Re-running install-system.sh
# overwrites this file. To customize, drop overrides at
# /etc/systemd/system/collabhost.service.d/override.conf instead of editing
# this file directly.

[Unit]
Description=Collabhost
Documentation=https://github.com/${REPO}
After=network-online.target
Wants=network-online.target
# Rate-limit crash loops. Per systemd.unit(5), StartLimitBurst /
# StartLimitIntervalSec are [Unit]-section keys; placing them in [Service]
# silently no-ops with an "Unknown key name" warning in the journal.
StartLimitBurst=5
StartLimitIntervalSec=60

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_GROUP}
WorkingDirectory=${INSTALL_PREFIX}
ExecStart=${BIN_DIR}/collabhost

# Point Collabhost at the canonical system layout.
Environment="COLLABHOST_DATA_PATH=${DATA_DIR}"
Environment="COLLABHOST_USER_TYPES_PATH=${USER_TYPES_DIR}"
Environment="COLLABHOST_LOGS_PATH=${LOG_DIR}"
Environment="COLLABHOST_PROXY_STORAGE_PATH=${CADDY_STORAGE_DIR}"
# Single-file .NET hosts extract embedded native deps to \$HOME/.net by
# default. Our service user has --no-create-home, and ProtectHome=true below
# would block /home/* anyway. Pin extraction to a writable path under
# ReadWritePaths so the host doesn't probe \$HOME.
Environment="DOTNET_BUNDLE_EXTRACT_BASE_DIR=${DOTNET_BUNDLE_DIR}"
Environment="DOTNET_ENVIRONMENT=Production"
Environment="ASPNETCORE_ENVIRONMENT=Production"

# Privileged-port bind without root, without per-binary setcap. The kernel
# grants cap_net_bind_service to the process tree, which the bundled Caddy
# child inherits. This is the load-bearing line that closes card #214: Caddy
# never needs setcap on the binary itself, so a 'cp' upgrade cannot strip an
# xattr (because there is no xattr). CapabilityBoundingSet caps the upper
# bound so other code paths cannot accidentally request more.
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

Restart=on-failure
RestartSec=5

# Conservative process hygiene. ProtectSystem=strict + the explicit
# ReadWritePaths is the standard "service can write to its data dirs and
# nothing else" shape.
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=${DATA_ROOT} ${LOG_DIR} ${CONFIG_DIR}

[Install]
WantedBy=multi-user.target
UNIT

chown root:root "${UNIT_PATH}"
chmod 0644      "${UNIT_PATH}"

# ---- Activate ----------------------------------------------------------------

systemctl daemon-reload

# Enable + start. Idempotent on re-run (enable returns 0 when already enabled,
# restart cycles whether the unit was running or not).
echo "Enabling and starting collabhost.service..."
systemctl enable collabhost.service >/dev/null
if systemctl is-active --quiet collabhost.service; then
  echo "Restarting (was already active)..."
  systemctl restart collabhost.service
else
  systemctl start collabhost.service
fi

# ---- Summary -----------------------------------------------------------------

# Resolve bundled Caddy version for the Bundled: disclosure line. Non-fatal --
# if the binary won't execute, fall back to omitting the version number.
CADDY_VERSION=""
CADDY_VERSION_RAW="$("${BIN_DIR}/caddy" version 2>/dev/null | awk '{print $1; exit}')" || true
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
echo "Collabhost ${TAG} installed system-wide."
echo "${BUNDLED_LINE}"
echo "  Binaries:    ${BIN_DIR}/"
echo "  Config:      ${CONFIG_DIR}/appsettings.json"
echo "  Data:        ${DATA_ROOT}/  (owned by ${SERVICE_USER}:${SERVICE_GROUP})"
echo "  Logs:        ${LOG_DIR}/  +  journalctl -u collabhost -f"
echo "  Service:     ${UNIT_PATH}"
echo ""
if [ -n "${IS_REINSTALL}" ]; then
  echo "Reinstall: data and Caddy storage preserved; binaries + unit + wwwroot refreshed."
  echo "On first boot of a new binary, watch for migration backups under ${DATA_DIR}/backups/."
else
  echo "First boot: collabhost prints its admin key once to stdout."
  echo "  Capture it with: journalctl -u collabhost --since '5 min ago' | grep 'Collabhost admin key:'"
  echo "  See ${INSTALL_PREFIX}/INSTALL.md §2 for details."
fi
echo ""
echo "Verify with:"
echo "  systemctl status collabhost"
echo "  curl http://localhost:58400/api/v1/status"
echo ""
echo "Uninstall with:"
echo "  sudo systemctl disable --now collabhost"
echo "  sudo rm ${UNIT_PATH}"
echo "  sudo systemctl daemon-reload"
echo "  sudo rm -rf ${INSTALL_PREFIX} ${CONFIG_DIR} ${DATA_ROOT} ${LOG_DIR}"
echo "  sudo userdel ${SERVICE_USER}    # group goes with --user-group convention"
