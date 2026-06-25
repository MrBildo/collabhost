# shellcheck shell=bash
# stage-common.sh -- shared helpers for the Collabhost stage deploy kit (card #443).
#
# Sourced by deploy-stage.sh and seed-demo-apps.sh. Carries the logging helpers,
# the told-inputs (instance.env) loader, the SINGLE privileged primitive the deploy
# uses, and the wipe-safety guard. Linux-only by design -- this runs on the box,
# never on a contributor workstation.
#
# Privilege model (card #443, Theo's box-half security deviation from design 4.3):
# the deploy runs as the unprivileged `stage-deploy` user and reaches privilege
# through exactly ONE root-owned helper -- `sudo stage-privop <verb>`. The helper
# HARDCODES every stage path, so no path and no wildcard ever crosses the sudo
# boundary. (The earlier direct-sudo shape -- `sudo runuser/install/find <path>` --
# had '..'-traversal-exploitable wildcard args toward prod; that is precisely the
# shape this replaces.) Theo's /etc/sudoers.d/stage-deploy allowlists just one line:
# `sudo <stage-privop> *`. The verb menu (Theo's helper owns each verb's hardcoded
# paths + exact behavior):
#   install-artifacts  install-caddy  wipe-data  wipe-ca  seed-install
#   start | stop | restart | status   logs [N]
# App registration is NOT privileged -- it is an HTTP POST to the local control
# plane with the stage admin key (see seed-demo-apps.sh).

# --- logging -----------------------------------------------------------------

log()  { printf '[stage] %s\n'        "$*"; }
warn() { printf '[stage] WARN: %s\n'  "$*" >&2; }
die()  { printf '[stage] ERROR: %s\n' "$*" >&2; exit 1; }

require_cmd() {
  local c
  for c in "$@"; do
    command -v "$c" >/dev/null 2>&1 || die "required command not found on PATH: ${c}"
  done
}

# DRY_RUN is set by the caller (deploy-stage.sh --dry-run). When 1, the privileged
# primitive prints what it WOULD run and returns success -- the whole script becomes
# a read-only plan dump, runnable on any Linux/WSL workstation for smoke-testing.
: "${DRY_RUN:=0}"

# --- told inputs -------------------------------------------------------------

# load_instance_env <path>: source the root-written, stage-deploy-readable env file
# that names every stage path/port/key. Standard Hosting rule #1: the writable
# locations are TOLD, never discovered. Missing file => fail loud.
load_instance_env() {
  local f="$1"
  [ -n "$f" ] || die "instance env path is empty"
  [ -f "$f" ] || die "instance env not found: ${f} (stand-up must provide this told input)"
  # set -a so every assignment in the file is exported for child processes (the
  # seed subprocess, the build, curl).
  set -a
  # shellcheck disable=SC1090
  . "$f"
  set +a
}

# require_keys KEY...: fail loud naming every required-but-empty key at once.
require_keys() {
  local k missing=()
  for k in "$@"; do
    [ -n "${!k:-}" ] || missing+=("$k")
  done
  [ ${#missing[@]} -eq 0 ] || die "instance env missing required key(s): ${missing[*]}"
}

# --- privileged primitive ----------------------------------------------------

# Path to the root-owned helper -- the ONLY thing the kit ever sudo's. Theo's
# stand-up installs it at the default; overridable for local authoring.
STAGE_PRIVOP="${STAGE_PRIVOP:-/opt/collabhost-stage/deploy/stage-privop}"

run_priv() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would run (sudo): $*"
    return 0
  fi
  sudo "$@"
}

# privop <verb> [arg] -- the single privileged primitive. <verb> is one of the
# documented stage-privop verbs; the optional [arg] is a bounded value (e.g. the
# line count for `logs`). No stage path is ever passed -- the helper hardcodes them.
privop() {
  run_priv "${STAGE_PRIVOP}" "$@"
}

# svc <start|stop|restart|status> -- service-control verb (sugar over privop).
svc() { privop "$1"; }

# --- wipe-safety guard -------------------------------------------------------

# assert_wipe_target <abs-path>: classify a path as a legitimate stage wipe target,
# or refuse loudly. Under the stage-privop model the privileged wipe (`privop
# wipe-data` / `wipe-ca`) passes NO path -- the helper hardcodes the stage dirs --
# so this is now a PRE-FLIGHT CONTRACT CHECK: before invoking the path-less wipe
# verb, deploy-stage.sh runs this over the instance.env-declared stage dirs (which
# MUST mirror the helper's hardcoded paths) to catch a told-input that names a prod
# path. The load-bearing wall is Theo's helper (hardcoded stage paths, runs as the
# stage user, kernel-denied every prod byte) + the prod-pathless sudoers; this guard
# is the in-script third layer. Reads stage roots (STAGE_DATA_ROOT, STAGE_SRV) and
# prod anchors (PROD_DATA, PROD_PREFIX, PROD_CONFIG_DIR) from the instance env.
assert_wipe_target() {
  local target="$1" rp prod prp root rproot under=0

  [ -n "$target" ] || die "wipe guard: empty target"
  case "$target" in
    /*) ;;
    *) die "wipe guard: target is not absolute: ${target}" ;;
  esac
  case "$target" in
    *..*) die "wipe guard: target contains '..': ${target}" ;;
  esac

  rp="$(realpath -m -- "$target")"

  # Every legitimate stage path carries one of these markers; no prod path does.
  case "$rp" in
    *collabhost-stage* | /srv/stage | /srv/stage/*) ;;
    *) die "wipe guard: target lacks a stage marker (collabhost-stage | /srv/stage): ${rp}" ;;
  esac

  # Reject root and shallow system paths outright.
  case "$rp" in
    / | /usr | /usr/* | /etc | /var | /var/lib | /opt | /srv | /home | /root \
      | /bin | /sbin | /boot | /lib | /lib64 | /dev | /proc | /sys)
      die "wipe guard: target is a system / too-shallow path: ${rp}" ;;
  esac

  # Must live under an allowed stage root.
  for root in "${STAGE_DATA_ROOT:-}" "${STAGE_SRV:-}"; do
    [ -n "$root" ] || continue
    rproot="$(realpath -m -- "$root")"
    if [ "$rp" = "$rproot" ]; then under=1; break; fi
    case "${rp}/" in
      "${rproot}"/*) under=1; break ;;
    esac
  done
  [ "$under" -eq 1 ] || die "wipe guard: target not under an allowed stage root (${STAGE_DATA_ROOT:-} ${STAGE_SRV:-}): ${rp}"

  # Must not equal, contain, or be contained by any prod anchor. The trailing
  # slash on each side is load-bearing: it stops /var/lib/collabhost from matching
  # /var/lib/collabhost-stage (the next char is '-', not '/').
  for prod in "${PROD_DATA:-}" "${PROD_PREFIX:-}" "${PROD_CONFIG_DIR:-}" \
              /var/lib/collabhost /opt/collabhost /etc/collabhost; do
    [ -n "$prod" ] || continue
    prp="$(realpath -m -- "$prod")"
    [ "$rp" = "$prp" ] && die "wipe guard: target equals prod anchor ${prp}: ${rp}"
    case "${rp}/" in
      "${prp}"/*) die "wipe guard: target is inside prod anchor ${prp}: ${rp}" ;;
    esac
    case "${prp}/" in
      "${rp}"/*) die "wipe guard: target is an ancestor of prod anchor ${prp}: ${rp}" ;;
    esac
  done
}
