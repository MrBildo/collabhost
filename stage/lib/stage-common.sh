# shellcheck shell=bash
# stage-common.sh -- shared helpers for the Collabhost stage deploy kit (card #443).
#
# Sourced by deploy-stage.sh and seed-demo-apps.sh. Carries the logging helpers,
# the told-inputs (instance.env) loader, the two privileged primitives the deploy
# uses, and the wipe-safety guard. Linux-only by design -- this runs on the box,
# never on a contributor workstation.
#
# The two privileged primitives (and nothing else) the kit needs from sudo:
#   1. systemctl {start,stop,restart,status} <stage-service>
#   2. runuser -u <stage-user> -- <argv>   (every stage-tree file op runs AS the
#      stage service user, so the kernel's own ownership wall keeps it off prod)
# Theo's /etc/sudoers.d/stage-deploy allowlists exactly these. See stage/README.md.

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
# primitives print what they WOULD run and return success -- the whole script
# becomes a read-only plan dump, runnable on any workstation for smoke-testing.
: "${DRY_RUN:=0}"

# --- told inputs -------------------------------------------------------------

# load_instance_env <path>: source the root-written, stage-deploy-readable env file
# that names every stage path/port/service/key. Standard Hosting rule #1: the
# writable locations are TOLD, never discovered. Missing file => fail loud.
load_instance_env() {
  local f="$1"
  [ -n "$f" ] || die "instance env path is empty"
  [ -f "$f" ] || die "instance env not found: ${f} (stand-up must provide this told input)"
  # set -a so every assignment in the file is exported for child processes (the
  # seed subprocess, runuser, the binary's --merge-appsettings).
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

# --- privileged primitives ---------------------------------------------------

run_priv() {
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would run (sudo): $*"
    return 0
  fi
  sudo "$@"
}

# svc <verb> -- systemctl <verb> <STAGE_SERVICE>. Verb is constrained to the
# documented set by the dispatcher's sudoers; we never pass a prod service name.
svc() {
  [ -n "${STAGE_SERVICE:-}" ] || die "svc: STAGE_SERVICE not set"
  run_priv systemctl "$1" "${STAGE_SERVICE}"
}

# as_stage <argv> -- run a command AS the stage service user. Every stage-tree
# write/delete goes through here, so even a bug in the path math is caught by the
# kernel: the stage user owns no prod byte and cannot touch prod (0750 collabhost).
as_stage() {
  [ -n "${STAGE_USER:-}" ] || die "as_stage: STAGE_USER not set"
  run_priv runuser -u "${STAGE_USER}" -- "$@"
}

# --- wipe-safety guard -------------------------------------------------------

# assert_wipe_target <abs-path>: refuse, loudly, to let a clean-deploy wipe touch
# anything but a stage path. Defense-in-depth ON TOP OF the OS-perms prod-wall and
# the dispatcher's prod-pathless sudoers -- three independent layers. Reads the
# stage roots (STAGE_DATA_ROOT, STAGE_SRV) and prod anchors (PROD_DATA,
# PROD_PREFIX, PROD_CONFIG_DIR) from the instance env.
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

# wipe_dir_contents <abs-path>: guard, then delete everything under the dir (incl.
# dotfiles), keeping the dir itself (ownership/mode survive). Runs AS the stage
# user. No-op if the dir does not exist.
wipe_dir_contents() {
  local d="$1"
  assert_wipe_target "$d"
  if [ "${DRY_RUN}" -eq 1 ]; then
    log "DRY-RUN would wipe contents of: ${d}"
    return 0
  fi
  # -mindepth 1 keeps the directory; -delete removes children depth-first.
  as_stage find "$d" -mindepth 1 -depth -delete
}
