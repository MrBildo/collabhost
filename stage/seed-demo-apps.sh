#!/usr/bin/env bash
# seed-demo-apps.sh -- build + install + register the curated demo set (card #443).
#
# Reconciled to the stage-privop privilege model (Theo's box half):
#   * per-app artifact BUILDS run UNPRIVILEGED in the checkout (as stage-deploy);
#   * a single `sudo stage-privop seed-install` then copies the built demo trees
#     from <checkout>/stage/demo-apps into /srv/stage/<slug> (as the stage user --
#     the helper hardcodes both paths, no path crosses the sudo boundary);
#   * registration is an unprivileged HTTP POST to the local control plane with the
#     stage admin key (read from a 0600 file on disk, NOT the env).
#
# REGISTER-IF-ABSENT + idempotent -- already-registered slugs are skipped. Invoked
# by deploy-stage.sh on the default (wipe) path; also runnable directly for a manual
# re-seed:
#
#     COLLABHOST_STAGE_INSTANCE_ENV=/etc/collabhost-stage/instance.env \
#       bash /opt/collabhost-stage/deploy/seed-demo-apps.sh
#
# Toolchain rule: an app that declares a `build` it cannot satisfy (its `requires`
# tool is absent) is SKIPPED with a warning -- an un-built artifact would just 502.
# No-build apps (static-site, external-route) always register. `start` is
# best-effort (warn on failure). A genuine register HTTP failure is fatal.
#
# Demo dir name == slug (a reconcile constraint): stage-privop seed-install copies
# <checkout>/stage/demo-apps/<dir> -> /srv/stage/<dir>, and the registration
# artifact path resolves to /srv/stage/<slug>/<subpath>, so each demo's source dir
# name MUST equal its slug. The seed asserts this (the current manifest satisfies it).

set -euo pipefail

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/stage-common.sh
. "${KIT_DIR}/lib/stage-common.sh"

: "${DRY_RUN:=0}"
INSTANCE_ENV="${COLLABHOST_STAGE_INSTANCE_ENV:-/etc/collabhost-stage/instance.env}"
load_instance_env "${INSTANCE_ENV}"
require_keys STAGE_BASE_URL STAGE_ADMIN_KEY_FILE STAGE_SRV
require_cmd curl python3

# Default derives from the told-input STAGE_BUILD_ROOT (instance.env), NOT the
# deploy-internal derived STAGE_SRC -- the reconcile dropped STAGE_SRC from
# instance.env, so the documented standalone re-seed (which sets neither
# STAGE_DEMO_APPS_DIR nor STAGE_SRC) must reach the checkout via STAGE_BUILD_ROOT.
# The deploy path passes STAGE_DEMO_APPS_DIR explicitly, so this default is the
# standalone-run path only. (#443; Kai C-2.)
DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR:-${STAGE_BUILD_ROOT:-}/checkout/stage/demo-apps}"
MANIFEST="${DEMO_APPS_DIR}/manifest.json"
[ -f "${MANIFEST}" ] || die "demo-app manifest not found: ${MANIFEST}"

# The stage admin key is a told secret on disk (0600 stage-deploy), not in the env.
# v1.8.0: registration requires the Administrator role, which this key carries.
ADMIN_KEY=""
if [ "${DRY_RUN}" -eq 0 ]; then
  [ -r "${STAGE_ADMIN_KEY_FILE}" ] \
    || die "stage admin key not readable: ${STAGE_ADMIN_KEY_FILE} (stand-up must provision it 0600 stage-deploy)"
  ADMIN_KEY="$(cat "${STAGE_ADMIN_KEY_FILE}")"
fi

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# --- existing registrations (the idempotency key is AppListItem.Name == slug) ---

existing_slugs() {
  curl -fsS -H "X-User-Key: ${ADMIN_KEY}" "${STAGE_BASE_URL}/api/v1/apps" \
    | python3 -c '
import json, sys
doc = json.load(sys.stdin)
apps = doc.get("items", doc) if isinstance(doc, dict) else doc
for a in apps:
    print(a.get("name") or a.get("slug", ""))
'
}

# --- plan: emit one record per app that needs registering ---------------------
# Columns, separated by the ASCII Unit Separator (\x1f): slug, artifactSource,
# build, requires, start. US is used instead of TAB because TAB is IFS-whitespace,
# which `read` collapses -- runs of tabs would merge and empty fields would vanish,
# shifting the columns. US is non-whitespace, so empty fields survive intact.
# The per-app registration body (with {artifact} resolved to the absolute on-box
# path /srv/stage/<slug>/<subpath>) is written to ${WORK}/<slug>.json.

build_plan() {
  local existing_file="$1"
  python3 - "${MANIFEST}" "${existing_file}" "${STAGE_SRV}" "${WORK}" <<'PY'
import json, os, posixpath, sys

manifest_path, existing_path, srv, workdir = sys.argv[1:5]

with open(manifest_path, encoding="utf-8") as fh:
    manifest = json.load(fh)
with open(existing_path, encoding="utf-8") as fh:
    existing = {line.strip() for line in fh if line.strip()}

def substitute(node, artifact):
    if isinstance(node, str):
        return node.replace("{artifact}", artifact)
    if isinstance(node, list):
        return [substitute(x, artifact) for x in node]
    if isinstance(node, dict):
        return {k: substitute(v, artifact) for k, v in node.items()}
    return node

for app in manifest.get("apps", []):
    slug = app["slug"]
    if slug in existing:
        continue

    # POSIX join: the artifact is always an absolute path on the (Linux) box.
    subpath = app.get("artifactSubpath", "")
    artifact = posixpath.join(srv, slug, subpath) if subpath else posixpath.join(srv, slug)

    body = {
        "name": slug,
        "displayName": app.get("displayName", slug),
        "appTypeSlug": app["appType"],
        "values": substitute(app.get("values", {}), artifact),
    }
    with open(os.path.join(workdir, slug + ".json"), "w", encoding="utf-8") as out:
        json.dump(body, out)

    cols = [
        slug,
        app.get("artifactSource", ""),
        app.get("build", ""),
        app.get("requires", ""),
        "1" if app.get("start", True) else "0",
    ]
    sys.stdout.write("\x1f".join(cols) + "\n")
PY
}

requires_satisfied() {
  local req
  for req in $1; do
    command -v "${req}" >/dev/null 2>&1 || return 1
  done
  return 0
}

register_app() {
  local slug="$1" body="${WORK}/$1.json" resp="${WORK}/$1.resp" code
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would POST /api/v1/apps for ${slug}"
    return 0
  fi
  code="$(curl -sS -o "${resp}" -w '%{http_code}' \
    -X POST "${STAGE_BASE_URL}/api/v1/apps" \
    -H "X-User-Key: ${ADMIN_KEY}" \
    -H 'Content-Type: application/json' \
    --data-binary "@${body}")"
  [ "${code}" = "201" ] || die "register ${slug} failed: HTTP ${code}: $(cat "${resp}" 2>/dev/null)"
  log "registered ${slug}"
}

start_app() {
  local slug="$1" code
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would POST /api/v1/apps/${slug}/start"
    return 0
  fi
  code="$(curl -sS -o /dev/null -w '%{http_code}' \
    -X POST "${STAGE_BASE_URL}/api/v1/apps/${slug}/start" \
    -H "X-User-Key: ${ADMIN_KEY}")"
  case "${code}" in
    2*) log "started ${slug}" ;;
    *)  warn "start ${slug} returned HTTP ${code} (left registered, not running)" ;;
  esac
}

# --- plan (absent apps only) --------------------------------------------------

EXISTING_FILE="${WORK}/existing.txt"
if [ "${DRY_RUN}" -eq 1 ]; then
  : > "${EXISTING_FILE}"
else
  existing_slugs > "${EXISTING_FILE}" || die "could not list existing apps at ${STAGE_BASE_URL}"
fi

PLAN_FILE="${WORK}/plan.usv"
build_plan "${EXISTING_FILE}" > "${PLAN_FILE}"

SKIP_FILE="${WORK}/toolchain-skip.txt"
: > "${SKIP_FILE}"
SKIPPED_TOOLCHAIN=0

BUILD_FAIL_FILE="${WORK}/build-fail.txt"
: > "${BUILD_FAIL_FILE}"
SKIPPED_BUILD=0

# --- phase 1: build each absent app's artifact UNPRIVILEGED in the checkout ----

while IFS=$'\x1f' read -r slug artifact_source build requires start; do
  [ -n "${slug}" ] || continue

  # seed-install copies <demo-apps>/<dir> -> /srv/stage/<dir>; the artifact path is
  # /srv/stage/<slug>, so the demo source dir name must equal the slug.
  if [ -n "${artifact_source}" ] && [ "${artifact_source}" != "${slug}" ]; then
    die "demo '${slug}': artifactSource '${artifact_source}' must equal the slug (stage-privop seed-install preserves dir names)"
  fi

  if [ -n "${build}" ] && ! requires_satisfied "${requires}"; then
    warn "skip ${slug}: build needs '${requires}' which is not on PATH"
    printf '%s\n' "${slug}" >> "${SKIP_FILE}"
    SKIPPED_TOOLCHAIN=$((SKIPPED_TOOLCHAIN + 1))
    continue
  fi

  if [ -n "${build}" ]; then
    src="${DEMO_APPS_DIR}/${artifact_source}"
    [ -d "${src}" ] || die "artifact source missing: ${src}"
    if [ "${DRY_RUN}" -eq 1 ]; then
      log "DRY-RUN would build ${slug}: (cd ${src} && ${build})"
    else
      log "building ${slug}: ${build}"
      # A failed demo build is NON-FATAL: warn, skip this demo (never register an
      # un-built artifact -- it would just 502), and continue with the rest. Same
      # warn-skip-continue an absent toolchain gets above. The `|| build_rc=$?`
      # keeps `set -e` from aborting the whole seed on one bad demo build (#443).
      build_rc=0
      ( cd "${src}" && bash -c "${build}" ) || build_rc=$?
      if [ "${build_rc}" -ne 0 ]; then
        warn "skip ${slug}: build failed (exit ${build_rc})"
        printf '%s\n' "${slug}" >> "${BUILD_FAIL_FILE}"
        SKIPPED_BUILD=$((SKIPPED_BUILD + 1))
        continue
      fi
    fi
  fi
done < "${PLAN_FILE}"

# --- phase 2: install the built demo trees into /srv/stage (privileged) --------
# privop seed-install copies <checkout>/stage/demo-apps/* -> /srv/stage/* as the
# stage user. Skipped when nothing is absent (an empty plan needs no copy).

if [ -s "${PLAN_FILE}" ]; then
  log "seed-install: copying built demo artifacts into ${STAGE_SRV} (privop seed-install)"
  privop seed-install
fi

# --- phase 3: register + start each built app (unprivileged HTTP) --------------

REGISTERED=0
while IFS=$'\x1f' read -r slug artifact_source build requires start; do
  [ -n "${slug}" ] || continue
  grep -qxF "${slug}" "${SKIP_FILE}" && continue        # toolchain-skipped: artifact not built
  grep -qxF "${slug}" "${BUILD_FAIL_FILE}" && continue  # build-failed: artifact not built

  register_app "${slug}"
  REGISTERED=$((REGISTERED + 1))

  if [ "${start}" = "1" ]; then
    start_app "${slug}"
  fi
done < "${PLAN_FILE}"

# Count present-skips for the summary (apps already registered).
SKIPPED_PRESENT="$(python3 - "${MANIFEST}" "${EXISTING_FILE}" <<'PY'
import json, sys
manifest = json.load(open(sys.argv[1], encoding="utf-8"))
existing = {l.strip() for l in open(sys.argv[2], encoding="utf-8") if l.strip()}
print(sum(1 for a in manifest.get("apps", []) if a["slug"] in existing))
PY
)"

log "seed done: registered=${REGISTERED} already-present=${SKIPPED_PRESENT} skipped-toolchain=${SKIPPED_TOOLCHAIN} skipped-build=${SKIPPED_BUILD}"
if [ -s "${SKIP_FILE}" ]; then
  warn "skipped (toolchain absent): $(tr '\n' ' ' < "${SKIP_FILE}")"
fi
if [ -s "${BUILD_FAIL_FILE}" ]; then
  warn "skipped (build failed): $(tr '\n' ' ' < "${BUILD_FAIL_FILE}")"
fi
