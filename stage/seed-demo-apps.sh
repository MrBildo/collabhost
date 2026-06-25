#!/usr/bin/env bash
# seed-demo-apps.sh -- register the curated demo set into a stage instance (card #443).
#
# Data-driven + REGISTER-IF-ABSENT + idempotent. Reads stage/demo-apps/manifest.json,
# and for each app whose slug is not already registered: copies its artifact under
# /srv/stage/<slug> (as the stage user), optionally builds it, registers it via the
# public REST control plane (POST /api/v1/apps with X-User-Key), and best-effort
# starts it. Safe to re-run against a populated instance -- already-present slugs are
# skipped. Invoked by deploy-stage.sh on the default (wipe) path; also runnable
# directly for a manual re-seed:
#
#     COLLABHOST_STAGE_INSTANCE_ENV=/etc/collabhost-stage/instance.env \
#       bash /opt/collabhost-stage/deploy/seed-demo-apps.sh
#
# Toolchain rule: an app that declares a `build` step it cannot satisfy (its
# `requires` tool is absent) is SKIPPED with a warning -- registering an app whose
# artifact was never built would just 502. No-build apps (static-site, external-route)
# always register. `start` is best-effort (warn on failure), since a registered app
# may still fail to start for runtime reasons. A genuine register HTTP failure is fatal.

set -euo pipefail

KIT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=lib/stage-common.sh
. "${KIT_DIR}/lib/stage-common.sh"

: "${DRY_RUN:=0}"
INSTANCE_ENV="${COLLABHOST_STAGE_INSTANCE_ENV:-/etc/collabhost-stage/instance.env}"
load_instance_env "${INSTANCE_ENV}"
require_keys STAGE_BASE_URL STAGE_ADMIN_KEY STAGE_SRV STAGE_USER
require_cmd curl python3

DEMO_APPS_DIR="${STAGE_DEMO_APPS_DIR:-${STAGE_SRC:-}/stage/demo-apps}"
MANIFEST="${DEMO_APPS_DIR}/manifest.json"
[ -f "${MANIFEST}" ] || die "demo-app manifest not found: ${MANIFEST}"

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# --- existing registrations (the idempotency key is AppListItem.Name == slug) ---

existing_slugs() {
  curl -fsS -H "X-User-Key: ${STAGE_ADMIN_KEY}" "${STAGE_BASE_URL}/api/v1/apps" \
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
# path -- subpath folded in here) is written to ${WORK}/<slug>.json.

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
    -H "X-User-Key: ${STAGE_ADMIN_KEY}" \
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
    -H "X-User-Key: ${STAGE_ADMIN_KEY}")"
  case "${code}" in
    2*) log "started ${slug}" ;;
    *)  warn "start ${slug} returned HTTP ${code} (left registered, not running)" ;;
  esac
}

# --- main loop ----------------------------------------------------------------

EXISTING_FILE="${WORK}/existing.txt"
if [ "${DRY_RUN}" -eq 1 ]; then
  : > "${EXISTING_FILE}"
else
  existing_slugs > "${EXISTING_FILE}" || die "could not list existing apps at ${STAGE_BASE_URL}"
fi

REGISTERED=0
SKIPPED_PRESENT=0
SKIPPED_TOOLCHAIN=0

while IFS=$'\x1f' read -r slug artifact_source build requires start; do
  [ -n "${slug}" ] || continue

  if [ -n "${build}" ] && ! requires_satisfied "${requires}"; then
    warn "skip ${slug}: build needs '${requires}' which is not on PATH"
    SKIPPED_TOOLCHAIN=$((SKIPPED_TOOLCHAIN + 1))
    continue
  fi

  dest="${STAGE_SRV}/${slug}"

  # Copy the artifact tree into the stage-owned /srv/stage/<slug> (as the stage user).
  if [ -n "${artifact_source}" ]; then
    src="${DEMO_APPS_DIR}/${artifact_source}"
    [ -d "${src}" ] || die "artifact source missing: ${src}"
    if [ "${DRY_RUN}" -eq 1 ]; then
      log "DRY-RUN would copy ${src} -> ${dest}"
    else
      # $1/$2 expand inside the runuser'd shell (positional args), not here.
      # shellcheck disable=SC2016
      as_stage bash -c 'rm -rf "$2" && mkdir -p "$2" && cp -R "$1/." "$2/"' _ "${src}" "${dest}"
    fi
  fi

  # Optional build, run in the copied artifact dir AS the stage user.
  if [ -n "${build}" ]; then
    if [ "${DRY_RUN}" -eq 1 ]; then
      log "DRY-RUN would build ${slug}: (cd ${dest} && ${build})"
    else
      log "building ${slug}: ${build}"
      as_stage bash -c "cd \"\$1\" && ${build}" _ "${dest}"
    fi
  fi

  register_app "${slug}"
  REGISTERED=$((REGISTERED + 1))

  if [ "${start}" = "1" ]; then
    start_app "${slug}"
  fi
done < <(build_plan "${EXISTING_FILE}")

# Count present-skips for the summary (apps already registered).
SKIPPED_PRESENT="$(python3 - "${MANIFEST}" "${EXISTING_FILE}" <<'PY'
import json, sys
manifest = json.load(open(sys.argv[1], encoding="utf-8"))
existing = {l.strip() for l in open(sys.argv[2], encoding="utf-8") if l.strip()}
print(sum(1 for a in manifest.get("apps", []) if a["slug"] in existing))
PY
)"

log "seed done: registered=${REGISTERED} already-present=${SKIPPED_PRESENT} skipped-toolchain=${SKIPPED_TOOLCHAIN}"
