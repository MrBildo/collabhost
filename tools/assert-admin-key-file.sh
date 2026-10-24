#!/usr/bin/env bash
# Checks how a first run hands over the admin key Collabhost generates: in
# initial-admin-key.txt in the data directory, readable by its owner only, and
# never through a log. Used by the install checks in CI; run it as a user who
# can read the file (root for a system install).
#
# Usage:
#   assert-admin-key-file.sh --data-dir DIR [--owner USER] [--stdout FILE]
#                            [--journal FILE] [--url BASE]
#
#   --data-dir DIR  the data directory (the one holding collabhost.db).
#   --owner USER    the account that must own the file.
#   --stdout FILE   the process's captured standard output: it must carry the
#                   "Collabhost admin key:" line exactly once, with the key
#                   the file holds.
#   --journal FILE  a dump of the service's journal: it must not contain the
#                   key or the line, and must say where the file is.
#   --url BASE      a running instance: the key must authenticate there as an
#                   administrator.
#
# The key itself is never printed.
set -euo pipefail

main() {
  local data_dir="" owner="" stdout_file="" journal_file="" url=""
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --data-dir) data_dir="$2"; shift 2 ;;
      --owner)    owner="$2"; shift 2 ;;
      --stdout)   stdout_file="$2"; shift 2 ;;
      --journal)  journal_file="$2"; shift 2 ;;
      --url)      url="$2"; shift 2 ;;
      *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
  done
  if [[ -z "${data_dir}" ]]; then
    echo "--data-dir is required." >&2
    exit 2
  fi

  local marker="Collabhost admin key:"
  local key_file="${data_dir}/initial-admin-key.txt"
  local failed=0
  fail() { echo "FAIL: $*" >&2; failed=1; }
  pass() { echo "  ok: $*"; }

  if [[ -L "${key_file}" || ! -f "${key_file}" ]]; then
    echo "FAIL: ${key_file} is missing or is not a regular file." >&2
    ls -la "${data_dir}" >&2 || true
    exit 1
  fi
  pass "${key_file} exists"

  local lines key
  lines=$(wc -l < "${key_file}" | tr -d ' ')
  key=$(head -n 1 "${key_file}" | tr -d '\r')
  # A ULID: 26 characters of Crockford base32.
  if [[ "${lines}" -eq 1 && "${key}" =~ ^[0-9A-HJKMNP-TV-Z]{26}$ ]]; then
    pass "the file holds one key, shaped as a ULID"
  else
    fail "the file does not hold exactly one ULID-shaped line (${lines} line(s))"
  fi

  # Owner-only permissions. Windows has no mode bits to check here.
  local mode file_owner
  if stat -c '%a' "${key_file}" >/dev/null 2>&1; then
    mode=$(stat -c '%a' "${key_file}")
    file_owner=$(stat -c '%U' "${key_file}")
  else
    mode=$(stat -f '%Lp' "${key_file}")
    file_owner=$(stat -f '%Su' "${key_file}")
  fi
  if [[ "${mode}" == "600" ]]; then
    pass "mode is 600"
  else
    fail "mode is ${mode}, expected 600"
  fi
  if [[ -n "${owner}" ]]; then
    if [[ "${file_owner}" == "${owner}" ]]; then
      pass "owned by ${owner}"
    else
      fail "owned by ${file_owner}, expected ${owner}"
    fi
  fi

  if [[ -n "${stdout_file}" ]]; then
    local printed count
    count=$(grep -cF "${marker}" "${stdout_file}" || true)
    printed=$( { grep -F "${marker}" "${stdout_file}" || true; } | head -n 1 | sed "s/.*${marker} *//" | tr -d '\r ')
    if [[ "${count}" -eq 1 && "${printed}" == "${key}" ]]; then
      pass "standard output printed the key once, and it matches the file"
    else
      fail "standard output carried the key line ${count} time(s), or a different key than the file"
    fi
  fi

  if [[ -n "${journal_file}" ]]; then
    if [[ ! -s "${journal_file}" ]]; then
      fail "the journal dump ${journal_file} is empty, so nothing about it can be checked"
    else
      if grep -qF "${key}" "${journal_file}"; then
        fail "the journal contains the admin key"
      else
        pass "the journal does not contain the admin key"
      fi
      if grep -qF "${marker}" "${journal_file}"; then
        fail "the journal contains the '${marker}' line"
      else
        pass "the journal does not contain the '${marker}' line"
      fi
      # The log says where the key is instead; this also shows the dump is
      # the right journal.
      if grep -qF "saved in ${key_file}" "${journal_file}"; then
        pass "the journal says the key is saved in ${key_file}"
      else
        fail "the journal does not say the key is saved in ${key_file}"
      fi
    fi
  fi

  if [[ -n "${url}" ]]; then
    local role
    role=$(curl -fsS -H "X-User-Key: ${key}" "${url}/api/v1/auth/me" | jq -r '.role' || true)
    if [[ "${role}" == "administrator" ]]; then
      pass "the key authenticates as an administrator"
    else
      fail "the key did not authenticate as an administrator (role '${role}')"
    fi
  fi

  if [[ "${failed}" -ne 0 ]]; then
    echo "Admin key hand-over check failed." >&2
    exit 1
  fi
  echo "Admin key hand-over verified."
}

main "$@"
