#!/usr/bin/env bash
# Self-test for tools/lint-source-comment-refs.py.
#
# Drives the linter against fixtures written to a temp dir at run time -- never
# committed, and never under backend/ or frontend/src/, so the lint can prove
# itself RED-on-violation / GREEN-on-clean without committing a violating
# comment into scanned source (which would trip its own CI gate). The fixtures
# carry .cs / .tsx extensions so the lexer exercises both tiers.
#
# Usage: bash tools/test-source-comment-refs.sh   (exit 0 = all cases pass)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LINT="${SCRIPT_DIR}/lint-source-comment-refs.py"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

PY="${PYTHON:-python3}"
FAILED=0

# Run the linter in --files mode against one fixture; assert the exit code.
# expect_fail <name> <file>  -> expects exit 1 (a violation was caught)
# expect_pass <name> <file>  -> expects exit 0 (clean)
expect() {
  local want="$1" name="$2" file="$3" rc=0
  "${PY}" "${LINT}" --files "${file}" >/dev/null 2>&1 || rc=$?
  if [[ "${rc}" -eq "${want}" ]]; then
    echo "  ok (${name}): exit ${rc}"
  else
    echo "  FAIL (${name}): expected exit ${want}, got ${rc}" >&2
    "${PY}" "${LINT}" --files "${file}" || true
    FAILED=1
  fi
}
expect_fail() { expect 1 "$1" "$2"; }
expect_pass() { expect 0 "$1" "$2"; }

# --- Violation cases (must be caught -> exit 1) -----------------------------

printf '%s\n' '// Card #348' > "${WORK}/card_ref.cs"
printf '%s\n' '// see #220 for the rationale' > "${WORK}/bare_hash.cs"
printf '%s\n' '// spec §12' > "${WORK}/section_ref.cs"
printf '%s\n' '// per .agents/specs/foo.md' > "${WORK}/spec_path.cs"
# Block-comment continuation line carrying a bare ref (lexer must see it).
{ printf '%s\n' '/*'; printf '%s\n' ' * background, see #99'; printf '%s\n' ' */'; } > "${WORK}/block.tsx"
# JSX comment form {/* ... */}.
printf '%s\n' '<div>{/* Card #7 */}</div>' > "${WORK}/jsx.tsx"

echo "--- Violation cases (expect RED / exit 1) ---"
expect_fail "card-ref"          "${WORK}/card_ref.cs"
expect_fail "bare-hash"         "${WORK}/bare_hash.cs"
expect_fail "section-ref"       "${WORK}/section_ref.cs"
expect_fail "spec-path"         "${WORK}/spec_path.cs"
expect_fail "block-continuation" "${WORK}/block.tsx"
expect_fail "jsx-comment"       "${WORK}/jsx.tsx"

# --- Clean cases (must pass -> exit 0) --------------------------------------

# Hex colour in code (string), not a comment.
printf '%s\n' 'const c = "#123456";' > "${WORK}/hex_code.tsx"
# Full GitHub issue URL is an allowed, resolvable citation.
printf '%s\n' '// ref https://github.com/MrBildo/collabhost/issues/5' > "${WORK}/gh_url.cs"
# A URL fragment with #digits living inside a string must not trip the check.
printf '%s\n' 'var u = "http://example.com/page#42";' > "${WORK}/url_string.cs"
# C# preprocessor directives start with # but are code, not comments.
{ printf '%s\n' '#region thing'; printf '%s\n' '#pragma warning disable 1591'; printf '%s\n' '#endregion'; } > "${WORK}/pragma.cs"
# A comment with inline rationale (the sanctioned form) and no internal ref.
printf '%s\n' '// Caddy needs @id tags for per-route CRUD' > "${WORK}/rationale.cs"

echo "--- Clean cases (expect GREEN / exit 0) ---"
expect_pass "hex-in-code"       "${WORK}/hex_code.tsx"
expect_pass "github-issue-url"  "${WORK}/gh_url.cs"
expect_pass "url-in-string"     "${WORK}/url_string.cs"
expect_pass "pragma-region"     "${WORK}/pragma.cs"
expect_pass "inline-rationale"  "${WORK}/rationale.cs"

if [[ "${FAILED}" -ne 0 ]]; then
  echo "Self-test FAILED." >&2
  exit 1
fi
echo "All self-test cases pass."
