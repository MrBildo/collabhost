#!/usr/bin/env bash
# Drift lint: assert that the canonical systemd unit
# (systemd/collabhost.system.service) and the embedded heredoc inside
# docs/install-system.sh stay in lockstep.
#
# Card #248. Both copies are documented as needing to track each other; this
# script is the mechanical guard. It does NOT change the install archive
# contract -- the canonical unit lives in `systemd/`, the heredoc lives in the
# install script, neither one ships in the release archive (the 7-of-7 contract
# is locked).
#
# Strategy:
#   1. Render the heredoc the same way bash would: extract it, set the
#      variables install-system.sh sets at the top of the file, and let bash
#      itself perform substitution. (Per Remy's lesson 2026-05-03, envsubst
#      mishandles backslash escapes -- bash's own heredoc machinery is the
#      ground truth.)
#   2. Strip the file-header commentary from both files (the commentary is
#      legitimately different -- the canonical file has a long preamble
#      explaining its purpose; the heredoc has a short "installed by ... TAG"
#      banner that mentions overrides). Compare what comes after the first
#      `[Unit]` section header.
#   3. Diff. Any non-empty diff fails the lint.
#
# Usage:
#   ./tools/lint-systemd-unit-drift.sh        -- exits 0 on lockstep, non-zero otherwise.
#
# Run from any directory; resolves paths relative to the script.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

CANONICAL="${REPO_ROOT}/systemd/collabhost.system.service"
INSTALLER="${REPO_ROOT}/docs/install-system.sh"

if [[ ! -f "${CANONICAL}" ]]; then
  echo "Canonical unit not found: ${CANONICAL}" >&2
  exit 2
fi
if [[ ! -f "${INSTALLER}" ]]; then
  echo "Installer script not found: ${INSTALLER}" >&2
  exit 2
fi

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# ---- Extract heredoc body from install-system.sh ----------------------------

# The heredoc is opened by `cat > "${UNIT_PATH}" <<UNIT` and closed by a line
# whose ONLY content is `UNIT`. awk extracts the body between those markers.
HEREDOC_RAW="${WORK}/heredoc-raw.txt"
awk '
  /^cat > "\$\{UNIT_PATH\}" <<UNIT$/ { in_block = 1; next }
  in_block && /^UNIT$/                { in_block = 0; next }
  in_block                            { print }
' "${INSTALLER}" > "${HEREDOC_RAW}"

if [[ ! -s "${HEREDOC_RAW}" ]]; then
  echo "Could not extract heredoc body from ${INSTALLER}." >&2
  echo "Expected an opening line: cat > \"\${UNIT_PATH}\" <<UNIT" >&2
  echo "and a closing line containing only: UNIT" >&2
  exit 2
fi

# ---- Render the heredoc -----------------------------------------------------

# Mirror the variable definitions from install-system.sh's "Defaults" block.
# These MUST match what install-system.sh sets at install time. If a future
# change to install-system.sh introduces a new substitution variable, add it
# here too.
SERVICE_USER="collabhost"
SERVICE_GROUP="collabhost"
INSTALL_PREFIX="/opt/collabhost"
BIN_DIR="${INSTALL_PREFIX}/bin"
CONFIG_DIR="/etc/collabhost"
DATA_ROOT="/var/lib/collabhost"
DATA_DIR="${DATA_ROOT}/data"
USER_TYPES_DIR="${DATA_ROOT}/user-types"
CADDY_STORAGE_DIR="${DATA_ROOT}/caddy"
DOTNET_BUNDLE_DIR="${DATA_ROOT}/dotnet-bundle"
LOG_DIR="/var/log/collabhost"
APPSETTINGS_DST="${CONFIG_DIR}/appsettings.json"
REPO="MrBildo/collabhost"
TAG="vX.Y.Z"   # Banner-only; the canonical unit doesn't carry a tag stamp.

export SERVICE_USER SERVICE_GROUP INSTALL_PREFIX BIN_DIR CONFIG_DIR
export DATA_ROOT DATA_DIR USER_TYPES_DIR CADDY_STORAGE_DIR DOTNET_BUNDLE_DIR
export LOG_DIR APPSETTINGS_DST REPO TAG

# Render via bash itself: write a tiny script that re-emits the heredoc body
# inside a fresh `cat <<UNIT ... UNIT` pair. Bash performs the substitution.
RENDER_SCRIPT="${WORK}/render.sh"
{
  echo '#!/usr/bin/env bash'
  echo 'cat <<UNIT'
  cat "${HEREDOC_RAW}"
  echo 'UNIT'
} > "${RENDER_SCRIPT}"
chmod +x "${RENDER_SCRIPT}"

RENDERED="${WORK}/heredoc-rendered.txt"
"${RENDER_SCRIPT}" > "${RENDERED}"

# ---- Normalize both sides for comparison -------------------------------------

# Drop the file-header commentary block before the first `[Unit]` header. The
# preamble legitimately differs (canonical has a long repo-purpose explainer;
# heredoc has a short installer banner mentioning overrides + TAG). Everything
# from `[Unit]` onward is the contract.
strip_preamble() {
  awk '/^\[Unit\]/{found=1} found{print}' "$1"
}

CANONICAL_BODY="${WORK}/canonical-body.txt"
RENDERED_BODY="${WORK}/rendered-body.txt"
strip_preamble "${CANONICAL}" > "${CANONICAL_BODY}"
strip_preamble "${RENDERED}"  > "${RENDERED_BODY}"

# ---- Diff --------------------------------------------------------------------

if diff -u "${CANONICAL_BODY}" "${RENDERED_BODY}" > "${WORK}/diff.txt"; then
  echo "OK: canonical systemd unit and install-system.sh heredoc are in lockstep."
  echo "  canonical: ${CANONICAL}"
  echo "  rendered : ${INSTALLER} (heredoc, with substitutions applied)"
  exit 0
fi

cat >&2 <<EOF
DRIFT: canonical systemd unit and install-system.sh heredoc differ after
substitution. Both must change together. See:
  canonical: ${CANONICAL}
  installer: ${INSTALLER}  (heredoc starting around 'cat > "\${UNIT_PATH}" <<UNIT')

--- diff (canonical vs rendered heredoc) ---
EOF
cat "${WORK}/diff.txt" >&2
exit 1
