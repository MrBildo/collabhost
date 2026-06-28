#!/usr/bin/env python3
"""Source-comment hygiene lint.

Published source (the FOSS) must be resolvable by an external reader. An
internal-only reference -- a bare card number, an `.agents/specs` path, a `§N`
section pointer -- means nothing to someone reading the shipped code on GitHub.
This lint fails a PR when an added/modified line introduces one of those
references *inside a source comment*, across both tiers
(backend/**/*.cs and frontend/src/**/*.{ts,tsx}).

The correctness crux is comment-context matching. The forbidden patterns
(notably `#\\d+`) collide with perfectly legal code -- a CSS/TSX hex colour
("#123456"), a URL fragment in a string ("http://host/#42"), a C# `#region`.
So this script does NOT grep raw lines. It lexes each file with a small
C-family state machine (line comments, block comments, single/double/template
strings, C# verbatim and raw strings) and only ever tests the text that is
genuinely inside a comment. A `#\\d+` in a string or in code is invisible to
the check; the same token in a `//` or `/* */` comment is caught.

Allowed even though it contains digits: a full GitHub issue URL
(https://github.com/.../issues/N) -- a resolvable citation. It is stripped
from the comment text before the forbidden patterns run.

Modes:
  --diff <base-ref>   Scan only added/modified lines in the PR's changes
                      (git diff <base>...HEAD). This is the CI mode.
  --files <f> [f...]  Scan every line of the named files. Used by the self-test
                      to drive fixtures that live outside the scanned tree, so
                      the lint never has to commit a violating comment into
                      scanned source to test itself.

Exit codes: 0 = clean, 1 = violations found, 2 = usage/internal error.
"""

import re
import subprocess
import sys

# --- Scanned scope (exact to the card's stated tiers) -----------------------

def in_scope(path: str) -> bool:
    path = path.replace("\\", "/")
    if path.startswith("backend/") and path.endswith(".cs"):
        return True
    if path.startswith("frontend/src/") and (path.endswith(".ts") or path.endswith(".tsx")):
        return True
    return False

# --- Forbidden patterns (the tight mechanical core) -------------------------
# Order matters only for which label a line reports first; `#\d+` subsumes the
# explicit `[Cc]ard #\d+` form, but both are listed for clear reporting.

FORBIDDEN = [
    (re.compile(r"[Cc]ard\s+#\d+"), "card reference ('Card #N')"),
    (re.compile(r"#\d+"), "bare issue/card number ('#N')"),
    (re.compile(r"\.agents/specs"), "internal spec path ('.agents/specs')"),
    (re.compile(r"§\s*\d"), "internal section reference ('§N')"),
]

# A resolvable citation -- allowed, removed before the forbidden patterns run.
# The path is pinned to exactly owner/repo (each segment [\w.-]+, no slash) so a
# forbidden token can't ride inside the allowed URL: a greedy `[^\s)]+` would
# swallow `x/.agents/specs` or `x#5` ahead of `/issues/N` and smuggle it past
# the gate. owner/repo is the actual shape of a GitHub issue URL.
GITHUB_ISSUE_URL = re.compile(r"https?://github\.com/[\w.-]+/[\w.-]+/issues/\d+")

CONVENTION = (
    "Source comments must be resolvable by an external reader of the published "
    "source. Use inline rationale (the WHY in the comment) or a full GitHub "
    "issue URL (https://github.com/.../issues/N) -- never a bare '#N', "
    "'Card #N', a '.agents/specs' path, or a '§N' section reference."
)

# --- Comment lexer ----------------------------------------------------------
# Walks a whole file once and returns, per 1-based line number, the
# concatenated text that lies inside comments on that line. Everything that is
# code, a string literal, a char literal, or a template literal contributes
# NOTHING -- which is precisely why a `#123456` hex in a string or a `#region`
# directive can never be mistaken for a comment reference.
#
# Scope: this gate is comment-context-scoped by design. A directive *label* such
# as `#region Card #5` or `#pragma`/`#if` text is code, not a comment, so the
# lexer emits nothing for it and the gate does not cover it. That fuzzy tail
# (directive labels carrying internal refs in published source) is Part-2 scrub
# territory, not this lint's job.

def comment_text_by_line(source: str) -> dict[int, str]:
    out: dict[int, list[str]] = {}
    i = 0
    n = len(source)
    line = 1

    def emit(ln: int, ch: str) -> None:
        out.setdefault(ln, []).append(ch)

    while i < n:
        c = source[i]
        nxt = source[i + 1] if i + 1 < n else ""

        # Line comment // ... (also covers C# /// XML doc) -> to end of line.
        if c == "/" and nxt == "/":
            i += 2
            while i < n and source[i] != "\n":
                emit(line, source[i])
                i += 1
            continue

        # Block comment /* ... */ (also covers TSX {/* ... */} -- the braces are
        # code, the /* ... */ inside is the comment). May span many lines.
        if c == "/" and nxt == "*":
            i += 2
            while i < n and not (source[i] == "*" and i + 1 < n and source[i + 1] == "/"):
                if source[i] == "\n":
                    line += 1
                else:
                    emit(line, source[i])
                i += 1
            i += 2  # consume the closing */
            continue

        # C# verbatim string @"..." -- no backslash escapes; "" is a literal ".
        if c == "@" and nxt == '"':
            i += 2
            while i < n:
                if source[i] == '"':
                    if i + 1 < n and source[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if source[i] == "\n":
                    line += 1
                i += 1
            continue

        # C# raw string literal """ ... """ (3+ quotes). Closes on a run of the
        # same length. Content is never a comment.
        if c == '"' and nxt == '"' and i + 2 < n and source[i + 2] == '"':
            q = 0
            while i < n and source[i] == '"':
                q += 1
                i += 1
            run = 0
            while i < n:
                if source[i] == '"':
                    run += 1
                    if run == q:
                        i += 1
                        break
                else:
                    if source[i] == "\n":
                        line += 1
                    run = 0
                i += 1
            continue

        # Ordinary string / char / template literal -- skipped, not comment.
        if c in ('"', "'", "`"):
            quote = c
            body_start = i + 1  # first char after the opening quote
            i += 1
            while i < n:
                if source[i] == "\\":  # backslash escape
                    if source[i + 1 : i + 2] == "\n":
                        line += 1
                    i += 2
                    continue
                if source[i] == "\n":
                    # A backtick template literal legitimately spans newlines.
                    if quote == "`":
                        line += 1
                        i += 1
                        continue
                    # Desync recovery: a real single-line ' or " string ALWAYS
                    # closes on its own line (an unterminated one is a compile
                    # error). Reaching a newline still open means the opening
                    # quote was never a string delimiter -- a JSX-text
                    # contraction (don't, you're) or a regex literal (/it's/,
                    # /["x]/) -- and everything we just skipped was really code.
                    # Rewind to just past the opening quote and rescan the rest
                    # of the line as code so a trailing // or {/* */} comment is
                    # still seen. (line is NOT advanced: the newline that
                    # triggered the rewind has not been crossed yet.)
                    i = body_start
                    break
                if source[i] == quote:
                    i += 1
                    break
                i += 1
            continue

        if c == "\n":
            line += 1
        i += 1

    return {ln: "".join(parts) for ln, parts in out.items()}

# --- Violation check --------------------------------------------------------

def violations_in_text(text: str) -> list[str]:
    stripped = GITHUB_ISSUE_URL.sub("", text)
    found = []
    for pattern, label in FORBIDDEN:
        m = pattern.search(stripped)
        if m:
            found.append(f"{label}: matched '{m.group(0)}'")
    return found

def scan_file(path: str, allowed_lines: set[int] | None) -> list[tuple[int, str]]:
    """Return (line, message) violations. allowed_lines=None scans every line."""
    try:
        with open(path, encoding="utf-8") as handle:
            source = handle.read()
    except (OSError, UnicodeDecodeError) as exc:
        print(f"warning: could not read {path}: {exc}", file=sys.stderr)
        return []

    by_line = comment_text_by_line(source)
    results = []
    for ln, text in sorted(by_line.items()):
        if allowed_lines is not None and ln not in allowed_lines:
            continue
        for msg in violations_in_text(text):
            results.append((ln, msg))
    return results

# --- Git diff (changed-lines-only) ------------------------------------------

HUNK = re.compile(r"^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@")

def changed_lines(base_ref: str) -> dict[str, set[int]]:
    cmd = [
        "git", "diff", "--unified=0", "--no-color", "--diff-filter=AM",
        f"{base_ref}...HEAD", "--", "backend", "frontend/src",
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        print(f"error: git diff failed: {proc.stderr.strip()}", file=sys.stderr)
        sys.exit(2)

    result: dict[str, set[int]] = {}
    current: str | None = None
    for raw in proc.stdout.splitlines():
        if raw.startswith("+++ b/"):
            current = raw[6:]
            continue
        if raw.startswith("@@") and current is not None:
            m = HUNK.match(raw)
            if not m:
                continue
            start = int(m.group(1))
            count = int(m.group(2)) if m.group(2) is not None else 1
            if count == 0:
                continue  # pure deletion hunk
            result.setdefault(current, set()).update(range(start, start + count))
    return result

# --- Entry point ------------------------------------------------------------

def report(path: str, hits: list[tuple[int, str]]) -> None:
    for ln, msg in hits:
        print(f"{path}:{ln}: {msg}")

def main(argv: list[str]) -> int:
    if len(argv) < 2 or argv[1] not in ("--diff", "--files"):
        print(__doc__)
        print("usage: lint-source-comment-refs.py (--diff <base-ref> | --files <file>...)",
              file=sys.stderr)
        return 2

    total = 0

    if argv[1] == "--files":
        files = argv[2:]
        if not files:
            print("error: --files needs at least one path", file=sys.stderr)
            return 2
        for path in files:
            hits = scan_file(path, allowed_lines=None)
            if hits:
                report(path, hits)
                total += len(hits)
    else:  # --diff
        base_ref = argv[2] if len(argv) > 2 else "origin/main"
        for path, lines in sorted(changed_lines(base_ref).items()):
            if not in_scope(path):
                continue
            hits = scan_file(path, allowed_lines=lines)
            if hits:
                report(path, hits)
                total += len(hits)

    if total:
        print()
        print(f"FAIL: {total} unresolvable internal reference(s) in source comments.")
        print(CONVENTION)
        return 1

    print("OK: no unresolvable internal references in scanned source comments.")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))
