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
#   COLLABHOST_INSTALL_SCRIPT_BASE_URL=URL  -- override the script/lib download base URL; the host this
#                                              script self-fetches install-lib.sh from when it is not
#                                              co-located (default: GitHub Pages)
#   COLLABHOST_INSTALL_ALLOW_HTTP=1  -- accept a plain http:// override that is not this machine
#                                       (downloads are HTTPS-only otherwise; see check_download_url)
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

# The shared lib is sourced two ways:
#
#   1. Co-located: when install-lib.sh sits next to this script (a git clone, or
#      a deliberate two-file download), source it directly. This is also the path
#      CI exercises -- it runs the checked-out script from the repo where the lib
#      is co-located in docs/.
#
#   2. Self-fetched: the documented one-liners run this script standalone --
#      `curl ... install-system.sh -o /tmp/install-system.sh; sudo bash ...` or
#      `curl ... install-system.sh | sudo bash`. Neither brings the lib along,
#      and under a pipe $0 is "bash" so there is no script directory to look in.
#      In that case, fetch the lib from the same host this script is published on
#      and source the downloaded copy. Operators never need to fetch a second
#      file by hand.
#
# COLLABHOST_INSTALL_SCRIPT_BASE_URL overrides the publish host (for a mirror or
# for testing). It is the script/lib host (GitHub Pages), distinct from
# COLLABHOST_INSTALL_BASE_URL, which overrides the archive host (GitHub Releases).
INSTALL_SCRIPT_BASE_URL="${COLLABHOST_INSTALL_SCRIPT_BASE_URL:-https://mrbildo.github.io/collabhost}"

# ---- Download policy ---------------------------------------------------------

# Every download (the shared library, the archive, checksums.txt) goes over
# HTTPS: curl is limited to https for the request and for every redirect, with
# TLS 1.2 or newer. A plain http:// base URL is accepted in exactly two cases,
# and each is announced on the terminal:
#
#   * The host is this machine (localhost, 127.x.y.z or [::1]). The request
#     never leaves the host, so nothing on a network can read or alter it.
#     Redirects must still be HTTPS. This is the local-mirror and CI case.
#   * COLLABHOST_INSTALL_ALLOW_HTTP=1 is set. The operator accepts that the
#     archive and the checksum it is verified against both arrive unprotected,
#     so the checksum no longer proves anything.
#
# Anything else is refused before the download starts.
#
# Usage: check_download_url <label> <url>. Sets CURL_PROTO_ARGS (an array) for
# that URL, or exits non-zero. install-lib.sh calls it for the archive host.
# Keep this function identical in install.sh and install-system.sh: both need
# it before install-lib.sh is available.
check_download_url() {
  local label="$1" url="$2" authority host
  case "${url}" in
    https://*)
      CURL_PROTO_ARGS=(--proto '=https' --proto-redir '=https' --tlsv1.2)
      return 0
      ;;
    http://*) ;;
    *)
      echo "Refusing ${label}: '${url}' is not an https:// URL." >&2
      exit 1
      ;;
  esac

  # Host part, the way curl reads it: drop the path/query/fragment first, then
  # any userinfo (so http://127.0.0.1@example.com is example.com), then the port.
  authority="${url#http://}"
  authority="${authority%%[/?#]*}"
  authority="${authority##*@}"
  case "${authority}" in
    \[*) host="${authority%%]*}]" ;;
    *)   host="${authority%%:*}" ;;
  esac
  host="$(printf '%s' "${host}" | tr '[:upper:]' '[:lower:]')"

  if [ "${host}" = "localhost" ] || [ "${host}" = "[::1]" ] \
    || printf '%s' "${host}" | grep -Eq '^127\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$'; then
    echo "Note: ${label} uses plain HTTP to ${host}, which is this machine. Redirects must be HTTPS." >&2
    CURL_PROTO_ARGS=(--proto '=http,https' --proto-redir '=https' --tlsv1.2)
    return 0
  fi

  if [ "${COLLABHOST_INSTALL_ALLOW_HTTP:-}" = "1" ]; then
    echo "WARNING: ${label} uses plain HTTP to ${host} (COLLABHOST_INSTALL_ALLOW_HTTP=1)." >&2
    echo "  The archive and its checksum both arrive unprotected: anyone on the network path can replace them." >&2
    CURL_PROTO_ARGS=(--proto '=http,https' --proto-redir '=http,https' --tlsv1.2)
    return 0
  fi

  echo "Refusing ${label}: '${url}' is plain HTTP." >&2
  echo "  Use an https:// URL. A mirror on this machine (http://localhost, http://127.0.0.1) is accepted." >&2
  echo "  To download over plain HTTP from another host anyway, set COLLABHOST_INSTALL_ALLOW_HTTP=1." >&2
  exit 1
}

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
    echo "install-system.sh needs curl to fetch its shared library (install-lib.sh)." >&2
    echo "  Install curl and retry, or download both files side by side:" >&2
    echo "    curl -fsSL ${INSTALL_SCRIPT_BASE_URL}/install-lib.sh -o install-lib.sh" >&2
    echo "    curl -fsSL ${INSTALL_SCRIPT_BASE_URL}/install-system.sh -o install-system.sh" >&2
    exit 1
  fi
  if ! command -v mktemp >/dev/null 2>&1; then
    echo "install-system.sh needs mktemp to stage its shared library (install-lib.sh)." >&2
    exit 1
  fi

  local lib_url="${INSTALL_SCRIPT_BASE_URL}/install-lib.sh"
  local lib_tmp
  check_download_url "COLLABHOST_INSTALL_SCRIPT_BASE_URL" "${INSTALL_SCRIPT_BASE_URL}"
  lib_tmp="$(mktemp)"
  echo "Fetching installer library from ${lib_url}..."
  if ! curl "${CURL_PROTO_ARGS[@]}" -fsSL --retry 3 --retry-delay 2 "${lib_url}" -o "${lib_tmp}"; then
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
Collabhost installer (Linux) -- SYSTEM scope

Lays /opt/collabhost + /etc/collabhost + /var/lib/collabhost + a system-scope
systemd unit running under a dedicated 'collabhost' system user. Requires root.

Usage: install-system.sh [options]

Options:
  --version vX.Y.Z   Pin to a specific release tag (default: latest)
  --help             Print this message and exit

Environment:
  COLLABHOST_VERSION                  Same as --version
  COLLABHOST_INSTALL_BASE_URL         Override archive download base URL (default: GitHub Releases)
  COLLABHOST_INSTALL_SCRIPT_BASE_URL  Override script/lib download base URL -- the host this script
                                      self-fetches install-lib.sh from when not co-located (default: GitHub Pages)
  COLLABHOST_INSTALL_ALLOW_HTTP=1     Accept a plain http:// override pointing at another host. Downloads are
                                      HTTPS-only otherwise (http://localhost and http://127.0.0.1 are accepted).

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
# COLLABHOST_INSTALL_BASE_URL, COLLABHOST_INSTALL_SCRIPT_BASE_URL,
# COLLABHOST_INSTALL_ALLOW_HTTP) via explicit forwarding (-E is intentionally
# avoided).
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
    COLLABHOST_INSTALL_SCRIPT_BASE_URL="${COLLABHOST_INSTALL_SCRIPT_BASE_URL:-}" \
    COLLABHOST_INSTALL_ALLOW_HTTP="${COLLABHOST_INSTALL_ALLOW_HTTP:-}" \
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

# wwwroot.sha256 sidecar: build-time SHA-256 hash of the wwwroot/ tree, written
# by the publish workflow. Lives at the install prefix root so the UAT runbook
# can compare against /api/v1/version.wwwrootHash (#342). Optional for archives
# predating #342 -- absence is silent.
if [ -f "${EXTRACT_DIR}/wwwroot.sha256" ]; then
  install -m 0644 -o root -g root "${EXTRACT_DIR}/wwwroot.sha256" "${INSTALL_PREFIX}/wwwroot.sha256"
fi

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
# It is NOT in the release archive (the archive contents are a fixed list in
# publish.yml). We embed it here -- keep this in lockstep with
# systemd/collabhost.system.service in the repo. The two must be identical from
# the [Unit] header down once the variables below are substituted; only the
# leading commentary differs. tools/lint-systemd-unit-drift.sh enforces this in
# CI.
#
# This file is rewritten on every run, so re-running the installer over an
# existing install is how an existing install picks up unit changes (for
# example the sandboxing block). Operator drop-ins under
# ${UNIT_PATH}.d/ are left alone and still apply on top.
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
# silently no-ops with an "Unknown key name" warning in the journal. Restart=
# / RestartSec= below stay in [Service] -- those ARE Service keys.
StartLimitBurst=5
StartLimitIntervalSec=60

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_GROUP}
WorkingDirectory=${INSTALL_PREFIX}
ExecStart=${BIN_DIR}/collabhost

# Point Collabhost at the canonical system layout. Each path matches what
# install-system.sh creates and chowns. See INSTALL.md §5.4 for the full
# environment-variable reference.
Environment="COLLABHOST_DATA_PATH=${DATA_DIR}"
Environment="COLLABHOST_USER_TYPES_PATH=${USER_TYPES_DIR}"
Environment="COLLABHOST_LOGS_PATH=${LOG_DIR}"
Environment="COLLABHOST_PROXY_STORAGE_PATH=${CADDY_STORAGE_DIR}"
# Single-file .NET hosts extract embedded native deps to \$HOME/.net/... by
# default. The collabhost system user has --no-create-home, so \$HOME points at
# /home/collabhost which doesn't exist -- and even if it did, ProtectHome=true
# below makes /home/* invisible to the service. Pin extraction to a path under
# our existing ReadWritePaths so the host can land its native deps without
# touching \$HOME.
Environment="DOTNET_BUNDLE_EXTRACT_BASE_DIR=${DOTNET_BUNDLE_DIR}"
# Explicit content-root + config-file paths. Without these, the
# binary's default ContentRoot is AppContext.BaseDirectory (= /opt/collabhost/bin/),
# which is where the binary lives but NOT where the operator-facing config + Portal
# assets live. ASPNETCORE_CONTENTROOT pins ContentRoot to /opt/collabhost so wwwroot
# resolves to /opt/collabhost/wwwroot/ via ASP.NET's {ContentRoot}/wwwroot default,
# and COLLABHOST_CONFIG_PATH points the explicit AddJsonFile call at the operator-
# editable canonical location in /etc/collabhost.
Environment="ASPNETCORE_CONTENTROOT=${INSTALL_PREFIX}"
Environment="COLLABHOST_CONFIG_PATH=${APPSETTINGS_DST}"
# DOTNET_ENVIRONMENT keeps the production defaults from any dev-only overrides.
Environment="DOTNET_ENVIRONMENT=Production"
Environment="ASPNETCORE_ENVIRONMENT=Production"

# Privileged-port bind without root, without per-binary setcap. The kernel
# grants cap_net_bind_service to the process tree, which the bundled Caddy
# child inherits (CapabilityBoundingSet caps the upper bound so other code
# paths cannot accidentally request more). Caddy never needs setcap on the
# binary itself, so a \`cp\` upgrade cannot strip the xattr (because there is no
# xattr). Ambient capabilities survive NoNewPrivileges= below; file
# capabilities set with setcap would not.
AmbientCapabilities=CAP_NET_BIND_SERVICE
CapabilityBoundingSet=CAP_NET_BIND_SERVICE

# Restart on crash. RestartSec gives the host a moment to flush stderr / crash
# logs before systemd respawns. The matching StartLimitBurst /
# StartLimitIntervalSec live in [Unit] above (systemd's parser rejects them in
# [Service]).
Restart=on-failure
RestartSec=5

# Sandboxing. Collabhost and every app it supervises run inside this block.
#
# Filesystem: the whole tree is read-only except the two state roots.
# ${CONFIG_DIR} is deliberately NOT writable: the service only reads its
# config there, and the installer (as root, outside this unit) writes it.
# Files the service and its apps create are group-readable at most (UMask).
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ReadWritePaths=${DATA_ROOT} ${LOG_DIR}
UMask=0027
#
# Privilege: nothing in this tree can gain privileges through setuid/setgid
# binaries (sudo, su) or create setuid/setgid files.
NoNewPrivileges=yes
RestrictSUIDSGID=yes
#
# Kernel surfaces a web app has no business touching: hardware devices (a
# minimal /dev with null, zero, random, urandom, tty, pts, shm), kernel
# tunables, modules, logs, the cgroup tree, the clock and the hostname.
# Collabhost's own cgroup-based process containment is not available under
# the system unit in any case (the service cgroup is not delegated to it);
# systemd itself stops every process in the unit when the service stops.
PrivateDevices=yes
ProtectKernelTunables=yes
ProtectKernelModules=yes
ProtectKernelLogs=yes
ProtectControlGroups=yes
ProtectClock=yes
ProtectHostname=yes
#
# Other processes on the host are invisible in /proc (apps still see their
# own user's processes). No new namespaces (containers, unshare), no realtime
# scheduling, no personality changes, native-architecture syscalls only, and
# only the socket families web apps use (local, IPv4, IPv6, netlink for
# interface lookups).
ProtectProc=invisible
RestrictNamespaces=yes
RestrictRealtime=yes
LockPersonality=yes
SystemCallArchitectures=native
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6 AF_NETLINK

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
  echo "First boot: collabhost saves its admin key once, to ${DATA_DIR}/initial-admin-key.txt."
  echo "  Read it with: sudo cat ${DATA_DIR}/initial-admin-key.txt  (then delete the file)"
  echo "  No such file? Releases from before the key file logged the key to the journal instead:"
  echo "    journalctl -u collabhost --since '5 min ago' | grep 'Collabhost admin key:'"
  echo "  See ${INSTALL_PREFIX}/INSTALL.md §2 for details."
fi
echo ""
echo "Verify with:"
echo "  systemctl status collabhost"
echo "  curl http://localhost:58400/api/v1/status"
echo ""
echo "After registering apps, run 'sudo collabhost --update-hosts' so <slug>.collab.internal resolves from this host. See ${INSTALL_PREFIX}/INSTALL.md section 9.10.2."
echo ""
echo "Uninstall with:"
echo "  sudo systemctl disable --now collabhost"
echo "  sudo rm ${UNIT_PATH}"
echo "  sudo systemctl daemon-reload"
echo "  sudo rm -rf ${INSTALL_PREFIX} ${CONFIG_DIR} ${DATA_ROOT} ${LOG_DIR}"
echo "  sudo userdel ${SERVICE_USER}    # group goes with --user-group convention"
