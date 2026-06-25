#!/usr/bin/env bash
# deploy-stage.sh -- Collabhost stage deploy, build-from-ref ON THE BOX (card #443).
#
# Invoked by the root-owned SSH forced-command dispatcher
# (/usr/local/bin/stage-deploy-dispatch, Theo's half) as:
#
#     deploy-stage.sh --ref <branch|tag|sha> [--keep-data]
#
# The dispatcher has already allowlist-validated the arguments; this script
# re-validates them anyway (belt-and-suspenders, so it is safe if ever run
# directly). It streams its log to stdout/stderr -- which the dispatcher relays
# back over the SSH pipe -- and its exit code becomes the ssh exit code.
#
# What it does, in order:
#   1. parse + re-validate args; load the told-inputs (instance.env)
#   2. sync the source checkout to <ref> (clone-if-absent, else fetch + checkout)
#   3. build the REAL release archive from that ref, mirroring the prod publish
#      pipeline (vite build -> bundled Caddy -> wwwroot hash -> single-file
#      self-contained `dotnet publish` -> stage the 8-item archive -> verify it)
#   4. stop the stage service
#   5. swap in the new binary + caddy + wwwroot + docs (as the stage user)
#   6. refresh stage config (smart-merge the ref's shipped appsettings)
#   7. DEFAULT: wipe stage state (guarded) and re-seed the curated demo set
#      --keep-data: skip BOTH the wipe AND the seed (code-only swap)
#   8. start the stage service; wait for readiness
#   9. smoke-check the API + Portal
#
# The build runs as the invoking (stage-deploy) user in a stage-owned checkout;
# every privileged step routes through the two primitives in lib/stage-common.sh
# (systemctl <verb> <stage-service>, runuser -u <stage-user> -- ...). Linux-only.

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

# REF charset mirrors the dispatcher's allowlist exactly: must start alnum, then
# [A-Za-z0-9._/-], and must not contain '..' (no parent traversal / option smuggling).
validate_ref() {
  local r="$1"
  [ -n "$r" ] || die "--ref is required"
  case "$r" in
    *..*) die "--ref must not contain '..': ${r}" ;;
  esac
  if ! printf '%s' "$r" | grep -Eq '^[A-Za-z0-9][A-Za-z0-9._/-]*$'; then
    die "--ref has invalid characters (allowed: [A-Za-z0-9._/-], must start alnum): ${r}"
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
  STAGE_SERVICE STAGE_USER \
  STAGE_PREFIX STAGE_CONFIG STAGE_CONFIG_BASELINE \
  STAGE_DATA_ROOT STAGE_DATA STAGE_APP_DATA STAGE_CADDY_STORAGE STAGE_SRV \
  STAGE_SRC STAGE_REPO_URL STAGE_RID \
  STAGE_BASE_URL STAGE_ADMIN_KEY \
  PROD_DATA PROD_PREFIX PROD_CONFIG_DIR

# Optional knobs with defaults.
STAGE_CADDY_SOURCE="${STAGE_CADDY_SOURCE:-build}"   # build (mirror prod) | copy (prod's caddy)
PROD_CADDY_PATH="${PROD_CADDY_PATH:-/opt/collabhost/bin/caddy}"
STAGE_GIT_REMOTE="${STAGE_GIT_REMOTE:-origin}"
STAGE_WIPE_CADDY="${STAGE_WIPE_CADDY:-0}"           # 0 keeps the internal CA (and its browser trust) across wipes
STAGE_DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR:-${STAGE_SRC}/stage/demo-apps}"
STAGE_READY_TIMEOUT="${STAGE_READY_TIMEOUT:-90}"
STAGE_SMOKE_APP_URL="${STAGE_SMOKE_APP_URL:-}"      # optional through-proxy smoke (warn-only)

STAGE_BIN="${STAGE_PREFIX}/bin"
STAGE_WWWROOT="${STAGE_PREFIX}/wwwroot"
BUILD_WS="${STAGE_SRC}/.stage-build"

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
  # obj/, the Go module cache) survive for incremental speed. We do NOT `git clean`.
  git -C "${STAGE_SRC}" checkout --force --detach "$target"

  local sha
  sha="$(git -C "${STAGE_SRC}" rev-parse --short HEAD)"
  STAGE_VERSION="0.0.0-stage-${sha}"
  log "checked out ${REF} @ ${sha} (version stamp ${STAGE_VERSION})"
}

# --- 3. build the real archive (mirror publish.yml) --------------------------

build_archive() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN skip build; EXTRACT_DIR would be ${BUILD_WS}/extract"
    EXTRACT_DIR="${BUILD_WS}/extract"
    return 0
  fi

  require_cmd dotnet npm tar
  if [ "${STAGE_CADDY_SOURCE}" = "build" ] && ! command -v go >/dev/null 2>&1; then
    die "STAGE_CADDY_SOURCE=build needs Go on PATH (xcaddy). Install Go on the box, or set STAGE_CADDY_SOURCE=copy in ${INSTANCE_ENV}."
  fi

  local pub_dir="${BUILD_WS}/publish"
  local caddy_out="${BUILD_WS}/caddy-build"
  local archive_dir="${BUILD_WS}/archive-stage"
  local verify_dir="${BUILD_WS}/verify"
  EXTRACT_DIR="${BUILD_WS}/extract"

  rm -rf "${pub_dir}" "${archive_dir}" "${verify_dir}" "${EXTRACT_DIR}"
  mkdir -p "${BUILD_WS}" "${caddy_out}" "${archive_dir}/LICENSES" "${verify_dir}" "${EXTRACT_DIR}"

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
  chmod +x "${caddy_out}/caddy"

  # 3c. wwwroot content hash (the SAME script publish.yml shells; #342/#395).
  local wwwroot_hash
  wwwroot_hash="$(bash "${STAGE_SRC}/tools/compute-wwwroot-hash.sh" "${STAGE_SRC}/backend/Collabhost.Api/wwwroot")"
  log "build: wwwroot hash ${wwwroot_hash}"

  # 3d. Single-file self-contained publish, embedding the hash -- prod's exact shape.
  log "build: dotnet publish (single-file, self-contained, ${STAGE_RID})"
  dotnet publish "${STAGE_SRC}/backend/Collabhost.Api/Collabhost.Api.csproj" \
    -c Release \
    -r "${STAGE_RID}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:Version="${STAGE_VERSION}" \
    -p:WwwrootHash="${wwwroot_hash}" \
    -o "${pub_dir}"

  # 3e. Stage the 8 contract items (mirrors publish.yml "Stage archive").
  cp "${pub_dir}/collabhost"               "${archive_dir}/"
  cp "${pub_dir}/appsettings.json"         "${archive_dir}/"
  cp "${caddy_out}/caddy"                  "${archive_dir}/"
  cp "${STAGE_SRC}/release-assets/INSTALL.md"     "${archive_dir}/"
  cp "${STAGE_SRC}/release-assets/caddy-LICENSE"  "${archive_dir}/LICENSES/"
  cp "${STAGE_SRC}/release-assets/caddy-NOTICE"   "${archive_dir}/LICENSES/"
  cp -R "${pub_dir}/wwwroot"               "${archive_dir}/"
  printf '%s' "${wwwroot_hash}"          > "${archive_dir}/wwwroot.sha256"
  chmod +x "${archive_dir}/collabhost" "${archive_dir}/caddy"

  # 3f. Build the real tar, then verify the 8-item flat contract by extracting it
  # (mirrors publish.yml "Verify archive contents"). We install from the verified
  # EXTRACT_DIR -- the operator's actual extract path -- not from the stage dir.
  local archive="${BUILD_WS}/collabhost-${STAGE_VERSION}-${STAGE_RID}.tar.gz"
  tar -czf "${archive}" -C "${archive_dir}" \
    collabhost appsettings.json caddy INSTALL.md \
    LICENSES/caddy-LICENSE LICENSES/caddy-NOTICE wwwroot wwwroot.sha256
  tar -xzf "${archive}" -C "${EXTRACT_DIR}"

  verify_archive "${EXTRACT_DIR}" "${wwwroot_hash}"

  # Build outputs carry no secrets; make them world-readable so the swap (which
  # runs AS the stage user) can read them out of the stage-deploy-owned checkout.
  chmod -R a+rX "${EXTRACT_DIR}"
}

verify_archive() {
  local dir="$1" expect_hash="$2" problems=0 p
  for p in collabhost appsettings.json caddy INSTALL.md \
           LICENSES/caddy-LICENSE LICENSES/caddy-NOTICE wwwroot.sha256; do
    [ -f "${dir}/${p}" ] || { warn "archive missing: ${p}"; problems=1; }
  done
  [ -d "${dir}/wwwroot" ]            || { warn "archive missing: wwwroot/"; problems=1; }
  [ -f "${dir}/wwwroot/index.html" ] || { warn "archive missing: wwwroot/index.html"; problems=1; }
  [ -n "$(ls -A "${dir}/wwwroot/assets" 2>/dev/null)" ] || { warn "archive: wwwroot/assets empty"; problems=1; }

  local sidecar
  sidecar="$(cat "${dir}/wwwroot.sha256" 2>/dev/null || true)"
  if ! printf '%s' "${sidecar}" | grep -Eq '^[0-9a-f]{64}$'; then
    warn "wwwroot.sha256 malformed: '${sidecar}'"; problems=1
  elif [ "${sidecar}" != "${expect_hash}" ]; then
    warn "wwwroot.sha256 (${sidecar}) != computed (${expect_hash})"; problems=1
  fi

  [ "${problems}" -eq 0 ] || die "archive contract verification failed"
  log "archive contract verified: 8/8 items, flat layout, hash matches"
}

# --- 5/6. swap artifacts + refresh config ------------------------------------

swap_artifacts() {
  log "swap: installing binary + caddy + wwwroot + docs into ${STAGE_PREFIX} (as ${STAGE_USER})"
  as_stage install -m 0755 "${EXTRACT_DIR}/collabhost" "${STAGE_BIN}/collabhost"
  as_stage install -m 0755 "${EXTRACT_DIR}/caddy"      "${STAGE_BIN}/caddy"
  as_stage install -m 0644 "${EXTRACT_DIR}/INSTALL.md" "${STAGE_PREFIX}/INSTALL.md"

  # LICENSES: clear + repopulate, matching install-system.sh. The $1/$2 are meant to
  # expand inside the runuser'd shell (positional args), not in this shell.
  as_stage mkdir -p "${STAGE_PREFIX}/LICENSES"
  # shellcheck disable=SC2016
  as_stage bash -c 'rm -f "$1"/* && cp "$2"/caddy-LICENSE "$2"/caddy-NOTICE "$1"/' \
    _ "${STAGE_PREFIX}/LICENSES" "${EXTRACT_DIR}/LICENSES"

  # wwwroot: always overwrite (the Portal SPA must track the binary).
  as_stage rm -rf "${STAGE_WWWROOT}"
  as_stage cp -R "${EXTRACT_DIR}/wwwroot" "${STAGE_WWWROOT}"
  as_stage install -m 0644 "${EXTRACT_DIR}/wwwroot.sha256" "${STAGE_PREFIX}/wwwroot.sha256"
}

refresh_config() {
  # Smart-merge the ref's shipped appsettings into the stage config, preserving
  # the isolation knobs Theo set at stand-up (Proxy.ListenAddress=:8080,:8443,
  # BaseDomain, BinaryPath, DnsProvider) and adding any new keys the ref shipped.
  # Same library the binary uses for prod reinstalls. The config is a TOLD input:
  # if stand-up has not created it, fail loud rather than guessing isolation values.
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would merge ${EXTRACT_DIR}/appsettings.json -> ${STAGE_CONFIG}"
    return 0
  fi
  [ -f "${STAGE_CONFIG}" ] || die "stage config not found: ${STAGE_CONFIG} (stand-up must provision it with the isolation knobs)"
  log "config: smart-merge shipped appsettings into ${STAGE_CONFIG}"
  as_stage "${STAGE_BIN}/collabhost" --merge-appsettings \
    "${EXTRACT_DIR}/appsettings.json" "${STAGE_CONFIG}" --baseline "${STAGE_CONFIG_BASELINE}"
}

# --- 7. wipe state -----------------------------------------------------------

wipe_state() {
  log "wipe: clearing stage state (guarded, as ${STAGE_USER})"
  wipe_dir_contents "${STAGE_DATA}"
  wipe_dir_contents "${STAGE_APP_DATA}"
  wipe_dir_contents "${STAGE_SRV}"
  if [ "${STAGE_WIPE_CADDY}" = "1" ]; then
    warn "STAGE_WIPE_CADDY=1: wiping the internal CA -- the box will need to re-trust stage's CA"
    wipe_dir_contents "${STAGE_CADDY_STORAGE}"
  fi
}

# --- 8. start + wait ---------------------------------------------------------

start_service() {
  log "start: ${STAGE_SERVICE}"
  svc start
}

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
  # Diagnostics stay within the two privileged primitives: rather than `systemctl
  # status --no-pager` (which would either page in the SSH pipe or miss the exact
  # sudoers allowlist match), point the operator at the dispatcher's read verb.
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

  log "stop: ${STAGE_SERVICE}"
  svc stop || warn "stop returned non-zero (already stopped?) -- continuing"

  swap_artifacts
  refresh_config

  if [ "${KEEP_DATA}" -eq 1 ]; then
    log "--keep-data: preserving stage state; skipping wipe + seed"
  else
    wipe_state
  fi

  start_service
  wait_ready

  if [ "${KEEP_DATA}" -eq 0 ]; then
    log "seed: registering the curated demo set (register-if-absent)"
    if [ "${DRY_RUN}" -eq 1 ]; then
      log "DRY-RUN skip seed"
    else
      COLLABHOST_STAGE_INSTANCE_ENV="${INSTANCE_ENV}" \
      STAGE_DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR}" \
        bash "${KIT_DIR}/seed-demo-apps.sh"
    fi
  fi

  smoke

  log "deploy complete: ref=${REF} version=${STAGE_VERSION:-?} keep-data=${KEEP_DATA}"
}

main
