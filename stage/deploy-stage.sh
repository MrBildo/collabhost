#!/usr/bin/env bash
# deploy-stage.sh -- Collabhost stage deploy, build-from-ref ON THE BOX (card #443).
#
# Invoked by the root-owned SSH forced-command dispatcher (Theo's half) as:
#
#     deploy-stage.sh --ref <branch|tag|sha> [--keep-data]
#
# The dispatcher has already allowlist-validated the arguments; this script
# re-validates them anyway (belt-and-suspenders, so it is safe if ever run
# directly). It streams its log to stdout/stderr -- which the dispatcher relays
# back over the SSH pipe -- and its exit code becomes the ssh exit code.
#
# Privilege model (Theo's box-half security deviation -- see lib/stage-common.sh):
# the script runs as the unprivileged `stage-deploy` user. The build runs entirely
# unprivileged in a stage-deploy-owned checkout; every privileged step routes
# through ONE root-owned helper, `sudo stage-privop <verb>` (the helper hardcodes
# all stage paths -- no path crosses the sudo boundary). App registration is an
# unprivileged HTTP POST to the local control plane. Linux-only.
#
# What it does, in order:
#   1. parse + re-validate args; load the told-inputs (instance.env)
#   2. sync the source checkout to <ref> at /home/stage-deploy/build/checkout
#   3. build the REAL release output from that ref, mirroring the prod publish
#      (vite build -> bundled Caddy -> wwwroot hash -> single-file self-contained
#      `dotnet publish`) into /home/stage-deploy/build/publish, then verify it
#   4. stop the stage service                       (privop stop)
#   5. install the new binary + wwwroot + caddy      (privop install-artifacts,
#                                                     privop install-caddy)
#   6. DEFAULT: wipe stage state, guarded            (privop wipe-data [+ wipe-ca])
#      --keep-data: skip the wipe (and the seed) -- code-only swap
#   7. start the stage service; wait for readiness   (privop start)
#   8. DEFAULT: seed the curated demo set            (privop seed-install + HTTP register)
#   9. smoke-check the API + Portal
#
# NOTE (reconcile seam for Theo -- card #443): the earlier kit smart-merged the
# ref's shipped appsettings into the stage config each deploy (`collabhost
# --merge-appsettings`, run as the stage user). The stage-privop verb menu carries
# no config-merge verb and Theo's default deploy sequence omits it -- the stage
# config is owned by the stand-up (the unit's env block + a static appsettings),
# not merged per deploy. So this script no longer touches stage config. If a ref
# that ships a new appsettings key needs to reach the stage config, that wants a
# new stage-privop verb (Theo's call); flagged in the PR + on the card.

set -euo pipefail

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/stage-common.sh
. "${KIT_DIR}/lib/stage-common.sh"

# --- argument parsing --------------------------------------------------------

REF=""
KEEP_DATA=0
DRY_RUN=0
INSTANCE_ENV="${COLLABHOST_STAGE_INSTANCE_ENV:-/etc/collabhost-stage/instance.env}"

usage() {
  cat <<'EOF'
Usage: deploy-stage.sh --ref <branch|tag|sha> [--keep-data] [--dry-run]

  --ref <ref>     Branch, tag, or 40-char SHA to build and deploy. Required.
  --keep-data     Code-only swap: preserve stage state, skip the wipe AND the seed.
  --dry-run       Validate args + told-inputs + the wipe guard and print the plan;
                  perform no build, no sudo, no HTTP. (Local authoring aid; the
                  dispatcher never passes this -- its allowlist is --ref/--keep-data.)
EOF
}

# REF charset is byte-identical to Theo's dispatcher allowlist: must start alnum,
# then [A-Za-z0-9._/-], length <= 200, and must not contain '..' (no parent
# traversal / option smuggling). The dispatcher validates first (the security
# boundary); this re-validation keeps the script safe if ever invoked directly.
validate_ref() {
  local r="$1"
  [ -n "$r" ] || die "--ref is required"
  case "$r" in
    *..*) die "--ref must not contain '..': ${r}" ;;
  esac
  if ! printf '%s' "$r" | grep -Eq '^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$'; then
    die "--ref has invalid characters or is too long (allowed: [A-Za-z0-9._/-], must start alnum, <=200): ${r}"
  fi
}

while [ $# -gt 0 ]; do
  case "$1" in
    --ref)
      [ $# -ge 2 ] || die "--ref requires a value"
      REF="$2"; shift 2 ;;
    --ref=*)
      REF="${1#--ref=}"; shift ;;
    --keep-data)
      KEEP_DATA=1; shift ;;
    --dry-run)
      DRY_RUN=1; shift ;;
    --instance-env)
      [ $# -ge 2 ] || die "--instance-env requires a value"
      INSTANCE_ENV="$2"; shift 2 ;;
    -h|--help)
      usage; exit 0 ;;
    *)
      die "unknown argument: $1 (allowed: --ref, --keep-data)" ;;
  esac
done

validate_ref "$REF"

# --- told inputs -------------------------------------------------------------

load_instance_env "${INSTANCE_ENV}"

require_keys \
  STAGE_BUILD_ROOT STAGE_REPO_URL STAGE_RID \
  STAGE_DATA_ROOT STAGE_DATA STAGE_APP_DATA STAGE_CADDY_STORAGE STAGE_SRV \
  STAGE_BASE_URL STAGE_ADMIN_KEY_FILE \
  PROD_DATA PROD_PREFIX PROD_CONFIG_DIR

# Optional knobs with defaults.
STAGE_CADDY_SOURCE="${STAGE_CADDY_SOURCE:-build}"   # build (mirror prod) | copy (prod's caddy)
PROD_CADDY_PATH="${PROD_CADDY_PATH:-/opt/collabhost/bin/caddy}"
STAGE_GIT_REMOTE="${STAGE_GIT_REMOTE:-origin}"
STAGE_WIPE_CADDY="${STAGE_WIPE_CADDY:-0}"           # 0 keeps the internal CA (and its browser trust) across wipes
STAGE_READY_TIMEOUT="${STAGE_READY_TIMEOUT:-90}"
STAGE_SMOKE_APP_URL="${STAGE_SMOKE_APP_URL:-}"      # optional through-proxy smoke (warn-only)

# Build-output paths (stage-deploy-owned; Theo's stage-privop verbs read from here).
STAGE_SRC="${STAGE_BUILD_ROOT}/checkout"            # git checkout (curated demos at ./stage/demo-apps)
PUBLISH_DIR="${STAGE_BUILD_ROOT}/publish"           # install-artifacts / install-caddy read this
BUILD_WS="${STAGE_BUILD_ROOT}/work"                 # intermediate build scratch
STAGE_DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR:-${STAGE_SRC}/stage/demo-apps}"

# STAGE_RID (like every STAGE_*/PROD_* here) is a told input from the sourced
# instance.env; shellcheck can't see the source, hence the directive.
# shellcheck disable=SC2153
log "ref=${REF} keep-data=${KEEP_DATA} dry-run=${DRY_RUN} rid=${STAGE_RID} caddy-source=${STAGE_CADDY_SOURCE}"

# --- 2. sync source ----------------------------------------------------------

sync_source() {
  require_cmd git
  if [ ! -d "${STAGE_SRC}/.git" ]; then
    log "cloning ${STAGE_REPO_URL} -> ${STAGE_SRC} (first deploy)"
    [ "${DRY_RUN}" -eq 1 ] && { log "DRY-RUN skip clone"; return 0; }
    mkdir -p "$(dirname "${STAGE_SRC}")"
    git clone "${STAGE_REPO_URL}" "${STAGE_SRC}"
  fi

  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN skip fetch/checkout of ${REF}"
    STAGE_VERSION="0.0.0-stage-dryrun"
    return 0
  fi

  log "fetching ${STAGE_GIT_REMOTE} and resolving ${REF}"
  git -C "${STAGE_SRC}" fetch --prune --tags "${STAGE_GIT_REMOTE}"

  # Resolve <ref> uniformly across branch / tag / sha, preferring the remote
  # branch tip so `--ref main` deploys origin/main, not a stale local main.
  local target
  if git -C "${STAGE_SRC}" rev-parse --verify --quiet "${STAGE_GIT_REMOTE}/${REF}^{commit}" >/dev/null; then
    target="${STAGE_GIT_REMOTE}/${REF}"
  elif git -C "${STAGE_SRC}" rev-parse --verify --quiet "refs/tags/${REF}^{commit}" >/dev/null; then
    target="refs/tags/${REF}"
  elif git -C "${STAGE_SRC}" rev-parse --verify --quiet "${REF}^{commit}" >/dev/null; then
    target="${REF}"
  else
    die "could not resolve --ref '${REF}' to a commit on ${STAGE_GIT_REMOTE} (branch/tag/sha)"
  fi

  # --force resets tracked files to the ref; untracked build caches (node_modules,
  # obj/, the Go module cache, the demo build outputs) survive for incremental
  # speed. We do NOT `git clean`.
  git -C "${STAGE_SRC}" checkout --force --detach "$target"

  local sha
  sha="$(git -C "${STAGE_SRC}" rev-parse --short HEAD)"
  STAGE_VERSION="0.0.0-stage-${sha}"
  log "checked out ${REF} @ ${sha} (version stamp ${STAGE_VERSION})"
}

# --- 3. build the real publish output (mirror publish.yml) -------------------

build_archive() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN skip build; publish would land at ${PUBLISH_DIR}"
    return 0
  fi

  require_cmd dotnet npm tar
  if [ "${STAGE_CADDY_SOURCE}" = "build" ] && ! command -v go >/dev/null 2>&1; then
    die "STAGE_CADDY_SOURCE=build needs Go on PATH (xcaddy). Install Go on the box, or set STAGE_CADDY_SOURCE=copy in ${INSTANCE_ENV}."
  fi

  local caddy_out="${BUILD_WS}/caddy-build"

  rm -rf "${PUBLISH_DIR}" "${caddy_out}"
  mkdir -p "${PUBLISH_DIR}/LICENSES" "${caddy_out}"

  # 3a. Frontend (vite build) -> dist, copied where dotnet publish picks it up.
  log "build: frontend (npm ci + vite build)"
  ( cd "${STAGE_SRC}/frontend" && npm ci && npx vite build )
  rm -rf "${STAGE_SRC}/backend/Collabhost.Api/wwwroot"
  cp -R "${STAGE_SRC}/frontend/dist" "${STAGE_SRC}/backend/Collabhost.Api/wwwroot"

  # 3b. Bundled Caddy: build the ref's pinned core+plugins (faithful -- a caddy.version
  # bump in the ref is actually exercised on stage), or copy prod's bundled caddy.
  case "${STAGE_CADDY_SOURCE}" in
    build)
      log "build: bundled Caddy via tools/build-caddy.sh (ref pins)"
      OUTPUT_PATH="${caddy_out}/caddy" bash "${STAGE_SRC}/tools/build-caddy.sh" ;;
    copy)
      log "build: copying prod's bundled Caddy from ${PROD_CADDY_PATH}"
      [ -r "${PROD_CADDY_PATH}" ] || die "STAGE_CADDY_SOURCE=copy but ${PROD_CADDY_PATH} is not readable"
      cp "${PROD_CADDY_PATH}" "${caddy_out}/caddy" ;;
    *)
      die "STAGE_CADDY_SOURCE must be 'build' or 'copy', got: ${STAGE_CADDY_SOURCE}" ;;
  esac

  # 3c. wwwroot content hash (the SAME script publish.yml shells; #342/#395).
  local wwwroot_hash
  wwwroot_hash="$(bash "${STAGE_SRC}/tools/compute-wwwroot-hash.sh" "${STAGE_SRC}/backend/Collabhost.Api/wwwroot")"
  log "build: wwwroot hash ${wwwroot_hash}"

  # 3d. Single-file self-contained publish, embedding the hash -- prod's exact shape.
  # Lands collabhost + appsettings.json + wwwroot/ directly into PUBLISH_DIR.
  log "build: dotnet publish (single-file, self-contained, ${STAGE_RID}) -> ${PUBLISH_DIR}"
  dotnet publish "${STAGE_SRC}/backend/Collabhost.Api/Collabhost.Api.csproj" \
    -c Release \
    -r "${STAGE_RID}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:Version="${STAGE_VERSION}" \
    -p:WwwrootHash="${wwwroot_hash}" \
    -o "${PUBLISH_DIR}"

  # 3e. Assemble the rest of the publish dir the stage-privop install verbs read:
  # caddy (install-caddy), the docs + licenses + hash sidecar (install-artifacts).
  cp "${caddy_out}/caddy"                         "${PUBLISH_DIR}/caddy"
  cp "${STAGE_SRC}/release-assets/INSTALL.md"     "${PUBLISH_DIR}/"
  cp "${STAGE_SRC}/release-assets/caddy-LICENSE"  "${PUBLISH_DIR}/LICENSES/"
  cp "${STAGE_SRC}/release-assets/caddy-NOTICE"   "${PUBLISH_DIR}/LICENSES/"
  printf '%s' "${wwwroot_hash}"                 > "${PUBLISH_DIR}/wwwroot.sha256"
  chmod +x "${PUBLISH_DIR}/collabhost" "${PUBLISH_DIR}/caddy"

  verify_publish "${PUBLISH_DIR}" "${wwwroot_hash}"

  # Build outputs carry no secrets; make them world-readable so the privileged
  # install verbs (run by the root helper) can read them out of the
  # stage-deploy-owned tree regardless of how the helper drops privilege.
  chmod -R a+rX "${PUBLISH_DIR}"
}

# Verify the publish dir carries the contract install-artifacts / install-caddy
# read -- mirrors publish.yml "Verify archive contents", checked directly on the
# publish dir (no tar round-trip: the stage install verbs read this dir, not a tar).
verify_publish() {
  local dir="$1" expect_hash="$2" problems=0 p
  for p in collabhost caddy appsettings.json INSTALL.md \
           LICENSES/caddy-LICENSE LICENSES/caddy-NOTICE wwwroot.sha256; do
    [ -f "${dir}/${p}" ] || { warn "publish missing: ${p}"; problems=1; }
  done
  [ -d "${dir}/wwwroot" ]            || { warn "publish missing: wwwroot/"; problems=1; }
  [ -f "${dir}/wwwroot/index.html" ] || { warn "publish missing: wwwroot/index.html"; problems=1; }
  [ -n "$(ls -A "${dir}/wwwroot/assets" 2>/dev/null)" ] || { warn "publish: wwwroot/assets empty"; problems=1; }

  local sidecar
  sidecar="$(cat "${dir}/wwwroot.sha256" 2>/dev/null || true)"
  if ! printf '%s' "${sidecar}" | grep -Eq '^[0-9a-f]{64}$'; then
    warn "wwwroot.sha256 malformed: '${sidecar}'"; problems=1
  elif [ "${sidecar}" != "${expect_hash}" ]; then
    warn "wwwroot.sha256 (${sidecar}) != computed (${expect_hash})"; problems=1
  fi

  [ "${problems}" -eq 0 ] || die "publish contract verification failed"
  log "publish contract verified: collabhost + caddy + wwwroot, hash matches"
}

# --- 5. install artifacts ----------------------------------------------------

install_artifacts() {
  # Privileged install of the freshly-built collabhost binary + wwwroot + docs,
  # and the bundled caddy, into the stage prefix. The verbs hardcode the stage
  # paths; no path crosses sudo. The deploy builds/copies the caddy to land at
  # ${PUBLISH_DIR}/caddy, and the contract is that install-caddy installs THAT
  # binary (so STAGE_CADDY_SOURCE=build's ref-pinned caddy actually reaches stage).
  log "install: collabhost binary + wwwroot + docs (privop install-artifacts)"
  privop install-artifacts

  # SEAM (#443, flagged to Theo): the box helper's install-caddy must read
  # ${PUBLISH_DIR}/caddy. If it instead installs prod's bundled caddy directly,
  # that is BENIGN under =copy (publish/caddy IS prod's caddy) but SILENTLY WRONG
  # under =build -- the freshly-built, ref-pinned caddy is discarded and stage runs
  # prod's caddy. Surface it loudly here rather than let =build quietly no-op.
  if [ "${STAGE_CADDY_SOURCE}" = "build" ]; then
    warn "install-caddy must install ${PUBLISH_DIR}/caddy for STAGE_CADDY_SOURCE=build to take effect; if the box helper installs prod's caddy, the ref's Caddy pins are NOT exercised on stage (#443 -- helper fix is Theo's)"
  fi
  log "install: bundled caddy (privop install-caddy)"
  privop install-caddy
}

# --- 6. wipe state -----------------------------------------------------------

wipe_state() {
  # Pre-flight defense-in-depth (the third layer -- see assert_wipe_target): the
  # privileged wipe verbs pass NO path (the helper hardcodes the stage dirs), so
  # we validate the instance.env-declared stage dirs -- which MUST mirror those
  # hardcoded paths -- before invoking the verb, catching a told-input that names
  # a prod path. The real wall is the helper (stage paths hardcoded, run as the
  # stage user) + the prod-pathless sudoers.
  assert_wipe_target "${STAGE_DATA}"
  assert_wipe_target "${STAGE_APP_DATA}"
  # STAGE_SRV is a told input from instance.env (not a typo of the derived STAGE_SRC).
  # shellcheck disable=SC2153
  assert_wipe_target "${STAGE_SRV}"
  log "wipe: clearing stage state (privop wipe-data)"
  privop wipe-data

  if [ "${STAGE_WIPE_CADDY}" = "1" ]; then
    assert_wipe_target "${STAGE_CADDY_STORAGE}"
    warn "STAGE_WIPE_CADDY=1: wiping the internal CA (privop wipe-ca) -- the box will need to re-trust stage's CA"
    privop wipe-ca
  fi
}

# --- 7. start + wait ---------------------------------------------------------

wait_ready() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN skip readiness wait"
    return 0
  fi
  require_cmd curl
  log "wait: ${STAGE_BASE_URL}/api/v1/status (timeout ${STAGE_READY_TIMEOUT}s)"
  local i
  for i in $(seq 1 "${STAGE_READY_TIMEOUT}"); do
    if curl -fsS "${STAGE_BASE_URL}/api/v1/status" >/dev/null 2>&1; then
      log "ready after ${i}s"
      return 0
    fi
    sleep 1
  done
  # Diagnostics stay within the privileged helper: point the operator at the
  # dispatcher's read verb rather than shelling `systemctl status` here.
  die "stage not ready within ${STAGE_READY_TIMEOUT}s -- diagnose with: ssh stage-deploy@<box> stage-logs"
}

# --- 9. smoke ----------------------------------------------------------------

smoke() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN skip smoke"
    return 0
  fi
  require_cmd curl
  local body

  # API status (JSON).
  body="$(curl -fsS "${STAGE_BASE_URL}/api/v1/status")" || die "smoke: /api/v1/status unreachable"
  printf '%s' "${body}" | grep -q '"status"' || die "smoke: /api/v1/status missing status field: ${body}"

  # Portal root + a SPA deep link must both return the shell HTML.
  body="$(curl -fsS "${STAGE_BASE_URL}/")" || die "smoke: Portal root unreachable"
  printf '%s' "${body}" | grep -qi '<!doctype html' || die "smoke: Portal root not HTML (wwwroot unwired?)"
  body="$(curl -fsS "${STAGE_BASE_URL}/apps")" || die "smoke: /apps unreachable"
  printf '%s' "${body}" | grep -qi '<!doctype html' || die "smoke: SPA fallback not wired (/apps non-HTML)"

  # The embedded wwwroot hash proves the single-file + integrity path built right.
  body="$(curl -fsS "${STAGE_BASE_URL}/api/v1/version")" || die "smoke: /api/v1/version unreachable"
  if printf '%s' "${body}" | grep -q '"wwwrootHash"'; then
    log "smoke: /api/v1/version carries wwwrootHash"
  else
    warn "smoke: /api/v1/version has no wwwrootHash (dev-shaped build?)"
  fi

  log "smoke: API + Portal OK"

  # Optional: one app route THROUGH the stage proxy. Off by default because it
  # depends on the box's stage DNS + edge-bind, which is Theo's stand-up. Warn-only.
  if [ -n "${STAGE_SMOKE_APP_URL}" ]; then
    if curl -fsSk "${STAGE_SMOKE_APP_URL}" >/dev/null 2>&1; then
      log "smoke: through-proxy app route OK (${STAGE_SMOKE_APP_URL})"
    else
      warn "smoke: through-proxy app route did not respond (${STAGE_SMOKE_APP_URL}) -- check stage DNS / edge-bind"
    fi
  fi
}

# --- orchestration -----------------------------------------------------------

main() {
  sync_source
  build_archive

  log "stop: stage service (privop stop)"
  svc stop || warn "stop returned non-zero (already stopped?) -- continuing"

  install_artifacts

  if [ "${KEEP_DATA}" -eq 1 ]; then
    log "--keep-data: preserving stage state; skipping wipe + seed"
  else
    wipe_state
  fi

  log "start: stage service (privop start)"
  svc start
  wait_ready

  if [ "${KEEP_DATA}" -eq 0 ]; then
    log "seed: building + installing + registering the curated demo set"
    if [ "${DRY_RUN}" -eq 1 ]; then
      log "DRY-RUN skip seed"
    else
      COLLABHOST_STAGE_INSTANCE_ENV="${INSTANCE_ENV}" \
      STAGE_PRIVOP="${STAGE_PRIVOP}" \
      STAGE_DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR}" \
        bash "${KIT_DIR}/seed-demo-apps.sh"
    fi
  fi

  smoke

  log "deploy complete: ref=${REF} version=${STAGE_VERSION:-?} keep-data=${KEEP_DATA}"
}

main
