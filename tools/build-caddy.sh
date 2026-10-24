#!/usr/bin/env bash
# Build a local Caddy binary that matches the shipped CI build (core +
# plugins from caddy-plugins.txt) and drop it at tools/caddy/caddy.
#
# Most contributors don't need this -- the proxy defaults to Caddy's
# internal CA, which doesn't depend on any DNS plugin. Run this only if
# you're locally exercising the ACME branch (Proxy:DnsProvider set).
#
# Reads `caddy.version`, `xcaddy.version`, `go.version`,
# `caddy-plugins.txt`, and `caddy-replaces.txt` at the repo root. Installs the pinned xcaddy into
# GOPATH/bin. The Go toolchain in `go.version` is the one the release
# pipeline compiles Caddy with; this script selects it through GOTOOLCHAIN,
# so any Go 1.21+ on PATH downloads and uses the pinned toolchain.
# Cross-compile is left to the operator -- this script targets the host
# OS/arch.
#
# Requires Go 1.21 or later (https://go.dev/dl/) on PATH.

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
GO_VERSION="$(read_pin go.version)"

echo "Caddy core:   v${CADDY_VERSION}"
echo "xcaddy:       v${XCADDY_VERSION}"
echo "Go toolchain: go${GO_VERSION}"

# Every go and xcaddy invocation below runs on the pinned toolchain. A bare
# version (no +auto suffix) also stops a module from switching to a newer one,
# so a dependency that needs a later Go fails the build instead of silently
# changing what gets compiled.
export GOTOOLCHAIN="go${GO_VERSION}"

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

# Parse caddy-replaces.txt: two columns, <go-module-path> <version>. Each
# becomes an xcaddy --replace; REPLACE_PAIRS keeps "module version" for the
# post-build check.
REPLACES_FILE="${REPO_ROOT}/caddy-replaces.txt"
if [[ ! -f "${REPLACES_FILE}" ]]; then
    echo "Pin file not found: ${REPLACES_FILE}" >&2
    exit 1
fi
REPLACE_ARGS=()
REPLACE_PAIRS=()
while IFS= read -r line || [[ -n "${line}" ]]; do
    trimmed="${line#"${line%%[![:space:]]*}"}"
    [[ -z "${trimmed}" || "${trimmed:0:1}" == "#" ]] && continue

    # shellcheck disable=SC2206
    parts=( ${trimmed} )
    if (( ${#parts[@]} != 2 )); then
        echo "Malformed replace line: ${line}" >&2
        exit 1
    fi
    REPLACE_ARGS+=( --replace "${parts[0]}=${parts[0]}@${parts[1]}" )
    REPLACE_PAIRS+=( "${parts[0]} ${parts[1]}" )
done < "${REPLACES_FILE}"

echo "Module overrides:"
for pair in ${REPLACE_PAIRS[@]+"${REPLACE_PAIRS[@]}"}; do
    echo "  ${pair% *} => ${pair#* }"
done

GOPATH_DIR="$(go env GOPATH)"
XCADDY_BIN="${GOPATH_DIR}/bin/xcaddy"

# Always install the pinned xcaddy: an older one left in GOPATH/bin by an
# earlier pin would otherwise keep being used after a bump.
echo "Installing xcaddy v${XCADDY_VERSION} ..."
go install "github.com/caddyserver/xcaddy/cmd/xcaddy@v${XCADDY_VERSION}"

WITH_ARGS=()
for spec in "${PLUGIN_SPECS[@]}"; do
    WITH_ARGS+=( --with "${spec}" )
done

mkdir -p "$(dirname "${OUTPUT_PATH}")"

echo
echo "Building Caddy v${CADDY_VERSION} -> ${OUTPUT_PATH}"
# The ${arr[@]+...} form keeps an empty array safe under `set -u` on bash 3.2.
"${XCADDY_BIN}" build "v${CADDY_VERSION}" "${WITH_ARGS[@]}" ${REPLACE_ARGS[@]+"${REPLACE_ARGS[@]}"} --output "${OUTPUT_PATH}"

# The binary records the toolchain and module versions it was built from;
# check them against the pins.
BUILD_INFO="$(go version -m "${OUTPUT_PATH}")"
BUILT_WITH="$(head -n 1 <<< "${BUILD_INFO}" | awk '{print $NF}')"
if [[ "${BUILT_WITH}" != "go${GO_VERSION}" ]]; then
    echo "Built Caddy reports toolchain '${BUILT_WITH}', expected 'go${GO_VERSION}' (go.version)" >&2
    exit 1
fi
echo "  ok: compiled with ${BUILT_WITH}"
for pair in ${REPLACE_PAIRS[@]+"${REPLACE_PAIRS[@]}"}; do
    module="${pair% *}"
    version="${pair#* }"
    if ! awk -v m="${module}" -v v="${version}" \
         '$1 == "=>" && $2 == m && $3 == v { found = 1 } END { exit !found }' <<< "${BUILD_INFO}"; then
        echo "Module override missing from built binary: ${module} ${version} (caddy-replaces.txt)" >&2
        exit 1
    fi
    echo "  ok: ${module} => ${version}"
done

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
