#!/usr/bin/env bash
# Build a local Caddy binary that matches the shipped CI build (core +
# plugins from caddy-plugins.txt) and drop it at tools/caddy/caddy.
#
# Most contributors don't need this -- the proxy defaults to Caddy's
# internal CA, which doesn't depend on any DNS plugin. Run this only if
# you're locally exercising the ACME branch (Proxy:DnsProvider set).
#
# Reads `caddy.version`, `xcaddy.version`, and `caddy-plugins.txt` at the
# repo root. Installs xcaddy into GOPATH/bin if missing. Cross-compile is
# left to the operator -- this script targets the host OS/arch.
#
# Requires Go (https://go.dev/dl/) on PATH.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
OUTPUT_PATH="${OUTPUT_PATH:-${SCRIPT_DIR}/caddy/caddy}"

if ! command -v go >/dev/null 2>&1; then
    echo "Go is not installed or not on PATH. See https://go.dev/dl/." >&2
    exit 1
fi

read_pin() {
    local file="${REPO_ROOT}/$1"
    if [[ ! -f "${file}" ]]; then
        echo "Pin file not found: ${file}" >&2
        exit 1
    fi
    tr -d '[:space:]' < "${file}"
}

CADDY_VERSION="$(read_pin caddy.version)"
XCADDY_VERSION="$(read_pin xcaddy.version)"

echo "Caddy core:   v${CADDY_VERSION}"
echo "xcaddy:       v${XCADDY_VERSION}"

PLUGINS_FILE="${REPO_ROOT}/caddy-plugins.txt"
if [[ ! -f "${PLUGINS_FILE}" ]]; then
    echo "Pin file not found: ${PLUGINS_FILE}" >&2
    exit 1
fi

# Parse caddy-plugins.txt: skip blank lines and comments, collect three
# whitespace-separated columns. PLUGIN_SPECS holds module@version values
# for `xcaddy build --with`; CADDY_IDS holds the post-build assertion list.
PLUGIN_SPECS=()
CADDY_IDS=()
while IFS= read -r line || [[ -n "${line}" ]]; do
    # Trim and skip blanks/comments.
    trimmed="${line#"${line%%[![:space:]]*}"}"
    [[ -z "${trimmed}" || "${trimmed:0:1}" == "#" ]] && continue

    # shellcheck disable=SC2206
    parts=( ${trimmed} )
    if (( ${#parts[@]} < 2 )); then
        echo "Malformed plugin line: ${line}" >&2
        exit 1
    fi
    PLUGIN_SPECS+=( "${parts[0]}@${parts[1]}" )
    if (( ${#parts[@]} >= 3 )); then
        CADDY_IDS+=( "${parts[2]}" )
    fi
done < "${PLUGINS_FILE}"

echo "Plugins:"
for spec in "${PLUGIN_SPECS[@]}"; do
    echo "  ${spec}"
done

GOPATH_DIR="$(go env GOPATH)"
XCADDY_BIN="${GOPATH_DIR}/bin/xcaddy"

if [[ ! -x "${XCADDY_BIN}" ]]; then
    echo "Installing xcaddy v${XCADDY_VERSION} ..."
    go install "github.com/caddyserver/xcaddy/cmd/xcaddy@v${XCADDY_VERSION}"
fi

WITH_ARGS=()
for spec in "${PLUGIN_SPECS[@]}"; do
    WITH_ARGS+=( --with "${spec}" )
done

mkdir -p "$(dirname "${OUTPUT_PATH}")"

echo
echo "Building Caddy v${CADDY_VERSION} -> ${OUTPUT_PATH}"
"${XCADDY_BIN}" build "v${CADDY_VERSION}" "${WITH_ARGS[@]}" --output "${OUTPUT_PATH}"

echo
echo "Asserting baked-in plugin modules ..."
MODULES_OUT="$("${OUTPUT_PATH}" list-modules)"
for caddy_id in "${CADDY_IDS[@]}"; do
    if ! grep -Fxq "${caddy_id}" <<< "${MODULES_OUT}"; then
        echo "Plugin missing from built binary: ${caddy_id}" >&2
        exit 1
    fi
    echo "  ok: ${caddy_id}"
done

echo
echo "Built Caddy: ${OUTPUT_PATH}"
