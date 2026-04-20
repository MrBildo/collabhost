# Card #153 Release Pipeline Spec — Dana Review

**Reviewer:** Dana (frontend lead, operator-UX + docs-alignment lens)
**Spec:** `.agents/specs/release-pipeline.md` (1438 lines, branch `spec/153-release-pipeline`)
**Date:** 2026-04-17
**Scope:** Independent review, no coordination with Marcus's parallel pass. Review-only — no code, no spec edits.

---

## 1. Summary verdict

**Ship-ready with a handful of targeted fixes.** The spec is thorough, the phasing is sensible, and the "Aspire model verbatim" bet is the right one — piped one-liner for the demo gif, download-then-execute for the README body, SHA256 on both ends. It respects every locked decision and reads like somebody who's actually shipped OSS releases before.

From the #155 README-restructure seat, this spec gives me most of what I need. The install URLs are stable, the `--version` contract is crisp, `/api/v1/version` has an unambiguous shape, INSTALL.md's section list is close to what I'd want, and the env-var floor is enumerated explicitly. The seams where #153 hands off to #155 are named.

The concerns are mostly UX polish in places where the spec tilts too "backend-correct" at the expense of "what does the operator actually do with this." Four of them are real — admin key capture, archive filename versioning, `--version` format, and INSTALL.md content gaps. The rest are minor. None are blockers.

---

## 2. Strengths from an operator-UX angle

- **Checksum verification is built-in, not bolted-on.** Install scripts halt before extraction on mismatch. That's the right failure mode — no half-written `.collabhost/bin/` directory, no "it broke during install, now what" state. The Collaboard gap is closed cleanly.
- **Merge-safe update by construction.** Archive never contains `data/` or `appsettings.Local.json`, so there's literally nothing for the installer to accidentally clobber. That's a nicer property than "we carefully avoid overwriting." Constructive correctness beats defensive correctness.
- **The env-var floor is enumerated up front.** Five `COLLABHOST_*_PATH` variables with explicit names and scopes. #104 gets to build the precedence stack without having to reverse-engineer what variables already exist. Good interface discipline.
- **Escape-hatch priority chain is plainly stated.** `COLLABHOST_CADDY_PATH` → existing admin on `:2019` → bundled sidecar → legacy config. An operator already running Caddy doesn't get overridden. An operator who drops a custom binary via env var gets obeyed. The bundled sidecar is the *default*, not the *only*. Good UX hierarchy.
- **Anomaly surfacing (section 17.3).** Catching that CLAUDE.md's "Known Issues" list is stale re: the admin port — that's exactly the kind of cross-doc discipline we claim to have. A lot of specs would ignore that. This one files it.
- **Phased implementation plan reads credibly.** Phase 1 (version helper + CLI flag + endpoint) is shippable standalone and immediately useful. That's the discipline of someone who's been through enough release pipelines to know you want small, landing PRs that don't all hinge on each other.
- **Self-check step in the workflow (§15.3).** `Collabhost.Api --version` run inside CI, compared to the extracted tag. Prevents the "tag says 0.1.0, binary says 0.0.0" outcome. Cheap and high-value.
- **Native matrix runners (§3.6).** Choosing `windows-latest` / `macos-latest` / `ubuntu-latest` per leg instead of Collaboard's all-Linux shortcut is the right call, and the future-signing argument for it is airtight. Five extra CI minutes total.

---

## 3. Concerns (ranked)

### 3.1 Admin key capture on first run is undocumented for the non-terminal launch case [HIGH]

**What's wrong.** The spec inherits card #152's "log admin key to stdout" behavior and calls it done. For an operator who (a) double-clicks `Collabhost.Api.exe` from Explorer on Windows, (b) starts Collabhost as a detached background process, or (c) has a terminal with a short scrollback that gets blown out by Caddy startup noise before they see it — "look at stdout" isn't actually a capture strategy.

**Why it matters.** First-run is the single highest-stakes moment in the operator's life with this product. If they can't get a key they can't authenticate, they can't register an app, they can't use the product. They also can't un-register what just happened. They'll kill the process, re-run it, and the key won't regenerate (the user is already seeded). Now they're in a factory-reset-or-bust state.

For #155 this is the single biggest content hole. I will be writing "Here's your first-run experience" and I need this to work.

**Recommended change.** One of:
1. On first-run seed, also write the admin key to a well-known file path: `{DataPath}/first-run-admin-key.txt` (`COLLABHOST_DATA_PATH` respected). Log a line pointing at that file path in addition to the current stdout write. Delete the file on shutdown, or after successful first authentication. This is the "sticky note" pattern and it costs ~15 lines of code.
2. Alternative: print the key to stdout *and* to a log file at a predictable path, and document both locations in INSTALL.md.

Option 1 is better UX. Option 2 is a tolerable minimum. What's not tolerable is "stdout only, figure it out."

Tag this as a coordinated change with card #152's follow-up: the README section I just wrote assumes the operator can watch stdout. That's true during `dotnet run` dev. It's not true for a binary.

---

### 3.2 Archive filename omits version — re-download confusion guaranteed [HIGH]

**What's wrong.** The spec chooses `collabhost-linux-x64.tar.gz` (no version) over `collabhost-v0.1.0-linux-x64.tar.gz` to "match Collaboard." The rationale in §4 is "keeps install scripts simpler." That's true for the install script. It's also solving the wrong problem.

Here's the operator journey that breaks: I download `collabhost-linux-x64.tar.gz` today. In three months I download another one. Both sit in `~/Downloads/`. I run `tar tzf` on each to figure out which is newer and *neither archive knows its own version*. The binary inside knows, but I have to extract and run `--version` to find out. At which point I've already polluted my filesystem with a mystery version.

Multiply that by anyone who ever shares an archive out-of-band with a teammate, attaches it to a bug report, or keeps a local archive of past versions for rollback. Every single scenario where the archive travels *outside* of GitHub Releases is degraded.

**Why it matters for #155.** The README's install-section screenshots and documentation will likely show download links. Every time I show a URL in docs, I'm showing an operator-recognizable artifact name. `collabhost-v0.1.0-linux-x64.tar.gz` is self-documenting. `collabhost-linux-x64.tar.gz` makes me write a caption explaining that it's "version 0.1.0, but the filename doesn't say so."

**Recommended change.** Version the archive filename: `collabhost-v0.1.0-linux-x64.tar.gz`. The install script constructs the filename from `{tag}-{rid}.{ext}` — one extra variable substitution, not "complexity." The GitHub Release page can cleanly track versioned assets. The `.sha256` files pair the same way. Matches what rustup, Aspire, fly, and deno all do.

Yes, this diverges from Collaboard. Collaboard picked wrong. A release pipeline shouldn't copy a sibling project's naming convention when the sibling project's naming convention actively degrades out-of-band distribution. Name artifacts for the future they'll live in.

---

### 3.3 `--version` output format — `Collabhost 0.1.0` beats bare `0.1.0` for real users [MEDIUM]

**What's wrong.** Remy recommends bare `0.1.0`, flags it as an open question. His reasoning is "machine-consumption." Scripts want just the version.

That's a local optimum for one consumer (a shell script grepping output) at the cost of the primary consumer (a human operator looking at a terminal). The spec already has a machine-consumption channel: `GET /api/v1/version` returning `{"version": "0.1.0"}`. That's what a script should use when it has network access, and if it has a local binary, it can run `--version | grep -Eo '[0-9]+\.[0-9]+\.[0-9]+'` (one line of regex).

**The user story that breaks with bare output.** Operator runs `Collabhost.Api --version`, sees `0.1.0`, copies into a bug report. Bug report reader sees `0.1.0` with no context. Version of what? Collabhost? Caddy? A dependency? The bare number disconnects from the identity of the thing emitting it. Collaboard learned this and went with `Collaboard 1.0.0`. It's not worth being different here just for theoretical script cleanliness.

**Recommended change.** Ship `Collabhost 0.1.0`. Consistent with Collaboard. Self-documenting in bug reports, copy-paste logs, support tickets. Scripts needing just the number can do `cut -d' ' -f2` or regex — that's a trivial one-liner, and it's the *right* side of the trade-off to put the friction on.

I'd go further: consider `Collabhost v0.1.0` with the `v` prefix. It matches the Git tag format that's the source of truth. But I can live with `Collabhost 0.1.0`.

---

### 3.4 Gap in INSTALL.md outline — "What port am I actually running on?" [MEDIUM]

**What's wrong.** §13.1 lists a "Troubleshooting" section with 3-4 items, and "Port 443 already in use" is one of them. Missing from the outline:
- **How to change the API port.** Default is `58400` today. Operators who run anything else on that port need a clean answer. The spec mentions `"SelfPort": 58400` in `appsettings.json` but doesn't route it into INSTALL.md.
- **How to open the dashboard after install.** "Run the binary" is step 1. Step 2 should be "now open `http://localhost:58400` in your browser." Nowhere in the outlined INSTALL.md content does that appear.
- **First-run admin key capture** (per concern 3.1 above).
- **How to verify the install worked.** `curl http://localhost:58400/api/v1/status` returns JSON. Or `Collabhost.Api --version` prints the version. An operator deserves a positive signal.
- **How to retrieve logs if the binary crashes before they can read stdout.** Today: there isn't one. `DataPath` should probably contain a `logs/` subdirectory.

**Why it matters for #155.** The README will link to INSTALL.md for "after you've installed, read this." Anything missing from INSTALL.md falls back on the README. If INSTALL.md is thin, I end up rewriting this content in the README, and then they diverge over time. Let INSTALL.md own the post-install experience in full.

**Recommended change.** Expand §13.1 to include:

1. **Quick start** (existing — but add "open http://localhost:58400 in your browser" as step 3)
2. **Your admin key** (new top-level section — not buried under first-run)
3. **What's in this archive** (existing)
4. **Where Collabhost stores data** (new — `$HOME/.collabhost/bin/data/collabhost.db` by default, `COLLABHOST_DATA_PATH` override)
5. **Changing the API port** (new — `Proxy:SelfPort` or env var)
6. **Configuration** (existing)
7. **macOS first-run quarantine** (existing)
8. **Updating** (existing)
9. **Troubleshooting** (existing, expanded)
10. **Verifying the install** (new — `--version`, `/api/v1/status`)
11. **Uninstall** (existing)
12. **Verifying checksums** (existing)

The target of ~3-4 pages still fits. Just right-sized for post-download reading.

---

### 3.5 `install.sh` auto-clear of `com.apple.quarantine` — yes, but tell them [MEDIUM]

**What's wrong.** Remy proposes auto-clearing `com.apple.quarantine` in `install.sh` and flags it as a question. Two issues with the proposed behavior:
1. It runs silently. The script clears the attribute, then the operator hits an unrelated Gatekeeper problem on a subsequent macOS update and has no idea why things suddenly break.
2. `install.sh` only runs if the operator *uses* the install script. An operator who downloads the `.tar.gz` manually (e.g., because they're air-gapped or prefer to inspect archives) still eats the full Gatekeeper UX.

**Why it matters.** macOS is best-effort tier. Anyone running on macOS is already in a not-fully-supported configuration. Make their experience maximally explicit so they know what state the system is in.

**Recommended change.** 
- **Yes, auto-clear in `install.sh`** — small footgun reduction, matches Aspire behavior.
- **But say it out loud.** Print before running:
  ```
  macOS detected. Clearing com.apple.quarantine on Collabhost.Api and caddy (not notarized in v1).
  ```
  One line of stdout. Operator knows what just happened.
- **Document the manual path prominently in INSTALL.md.** Current §11.1 proposal is good, just make sure the "install.sh runs this for you" sentence is *secondary*, not primary. The primary audience for the INSTALL.md Gatekeeper section is someone who didn't use `install.sh`.

---

### 3.6 `/api/v1/version` is too thin to be useful [LOW-MEDIUM]

**What's wrong.** `{"version": "0.1.0"}`. That's it. Remy explicitly resists adding anything else. I get the purity argument ("single-purpose endpoint"), but the operator UX is weaker than it could be.

The use case for `/api/v1/version`: I need to know what's running on that host without logging in. Usually because I'm debugging. A one-field response means I can't tell at a glance:
- Whether this is a release build or dev build
- Whether the binary actually matches what the tag claims (commit hash would answer)
- When it was built
- Which platform it's running on

**Open question 6 already asks this.** Remy's proposal is "put diagnostics in `/api/v1/status`, keep `/api/v1/version` minimal." I disagree with the framing.

**Recommended change.** Expand `/api/v1/version` to:
```json
{
  "version": "0.1.0",
  "commit": "abc1234",
  "platform": "linux-x64"
}
```

Three fields. Still one conceptual thing: "what version am I?" Commit hash answers "did this binary come from the commit I expect?" Platform answers "am I on the right RID?" Both are trivial to add, both disambiguate real bug report scenarios.

`/api/v1/status` stays about runtime state (uptime, app counts, etc.). `/api/v1/version` becomes about build identity. Clean separation.

Adding later is additive and safe (consumers who read only `version` still work). Adding now is two extra field populations.

---

### 3.7 Install script error UX is underspecified [LOW-MEDIUM]

**What's wrong.** §9.5 covers the checksum-mismatch happy path (script prints error, exits). But what happens on:
- **Network failure during archive download** (`curl` fails, partial file written). Does the script clean up the partial? Is there a retry like the workflow's Caddy download? The spec doesn't say.
- **Permission denied on `$HOME/.collabhost/bin`** (unusual — e.g., if the directory exists and is owned by a different user from a prior install-as-root attempt). What does the operator see? Generic `cp` error? Something helpful?
- **Checksum file 404** (GitHub transient). Same as archive failure but at a different step. Same questions.
- **`$HOME` not set or inaccessible** (launchd context, certain containers). Is there a default fallback?
- **Shell RC file write failure** (e.g., `.zshrc` is a symlink to a read-only location). Does the install still succeed but PATH not get added? Is that obvious?

The spec shows the *mechanism* for checksum verification in detail. It does not show the user's experience when things fail.

**Why it matters for #155.** The README will have an install one-liner. When it fails, the operator's first stop is the README troubleshooting section. If INSTALL.md / README can't tell them what to do, they file an issue, or worse, give up.

**Recommended change.** Add §9.x "Error handling contract." Enumerate the failure modes above. For each, specify:
- Exit code
- Message format (human-readable, not raw `curl` stderr)
- What, if anything, the script cleans up before exiting
- Whether re-run is safe (should be: yes, for all of them)

Targets can be tight: "operator re-runs the one-liner, network transients clear, install succeeds on second try" is the shape. The script should be idempotent and self-cleaning.

---

### 3.8 GitHub Pages landing page content needs design review [LOW]

**What's wrong.** §10.4 proposes a minimal `docs/index.html` with system-font styling and a pair of `<pre>` blocks for install commands. It's functional but it's also:
- Not on-brand (no War Machine treatment, no IBM Plex Mono, no amber accents)
- Doesn't link to the repo's main narrative surfaces (README sections, MCP docs, social card)
- Has the install commands but not the checksum-verify shortcut for people who land there before they land on GitHub

**Why it matters.** Anyone landing on `mrbildo.github.io/collabhost/` is either (a) following a link from somewhere external, (b) attempting to download an install script and getting an index page instead, or (c) testing whether Pages is live. The page is small and has low traffic. But it's also the *one page I can't iterate on in a PR review cycle* — Pages updates are live the moment they merge.

**Recommended change.** Minimal viable landing page in this card is fine. **Flag it as a #155 design task** — I'll take ownership of styling it to match the social card (which is the existing War Machine external touchpoint). Content-wise, I'd add:
- Link to GitHub repo prominently above the fold
- Link to the social card / README for the narrative
- The two install commands (as proposed)
- A "verify before running" line with a link to the script source on GitHub

The HTML Remy sketches is good scaffolding. I'll make it look like a Collabhost page.

---

### 3.9 "Disable proxy, keep API running" softens Decision 10's "hard-fail" — ok with me [LOW]

**What's wrong.** Decision 10 says "hard-fail with clear log on unreachable." §6.4.2 softens this to "log fatal, disable proxy subsystem, keep API running." Remy flags it as open question 1.

**From the operator's perspective:** this is the right softening. A partially-broken Collabhost (dashboard works, registry works, log viewer works, no proxy routing) is *more useful* than a fully-dead Collabhost. The operator can open the dashboard, see the red banner saying "proxy is down," read the log, and fix Caddy while continuing to inspect their state. A hard-kill gives them no diagnostic surface at all.

**The UX requirement this creates:** there has to be a visible signal that proxy is down. The topbar needs a badge, or the Routes page needs an error banner, or the Dashboard stats need a "proxy: unavailable" indicator. If the proxy silently doesn't work and the operator registers an app expecting a route, they're confused for hours before realizing why.

**Recommended change.** 
- Support the soft-fail interpretation (keep API running).
- **Add to this spec:** "when proxy subsystem is disabled for the boot, the dashboard surfaces a visible indicator." The exact design is frontend work I'll own. The spec just needs to commit that the backend surfaces the state in `/api/v1/status` or similar, so the frontend has something to read.

---

### 3.10 `docs/` directory mixes install scripts and landing page with unrelated content [LOW]

**What's wrong.** Post-card, `docs/` will contain:
- `install.sh`, `install.ps1`, `index.html` (this card's deliverables)
- `screenshots/*.png` (existing — README assets)
- `social-preview.html`, `social-preview.png` (existing — OSS launch assets)

That's three distinct content types in one folder with no separation. If we ever want to suppress screenshots from being served by Pages (which we should — they're README assets, not website assets), we'll be restructuring after the fact.

**Why it matters.** GitHub Pages serves *everything* in the configured folder. Someone landing on `mrbildo.github.io/collabhost/screenshots/dashboard.png` gets the raw PNG. That's fine today but it's Pages leakage. And when #155 ships it'll likely want *more* Pages-hosted content (install scripts, possibly docs), making the sprawl worse.

**Recommended change (defer, not block).** Either:
- Move install scripts to `docs/install/install.sh` and landing to `docs/index.html`, leave README assets in `docs/screenshots/`. Structure within `docs/` is free.
- Or: keep as-is for this card, add a `.nojekyll` if not already present, and accept the mixed concerns. Note the cleanup as a #155 follow-up.

Not a blocker. Flagging because I'll see this again in three months and want to record the trade-off now.

---

## 4. The 6 open questions — my independent answers

### Q1: Post-launch Caddy probe — hard-fail or soft-fail?

**Soft-fail (Remy's interpretation).** Disable proxy subsystem, keep API running. My stronger-than-Marcus's-angle argument: the dashboard remaining functional is load-bearing for the "fix Caddy while continuing to inspect state" loop. A hard-kill takes away the operator's diagnostic surface at the exact moment they need it.

**But:** require a visible "proxy unavailable" indicator in the dashboard. If the proxy silently doesn't work, the operator registers an app, expects a route, and is confused. I'll own the frontend component; the spec needs to commit that `/api/v1/status` or equivalent exposes the disabled-state flag.

### Q2: `--version` stdout format

**`Collabhost 0.1.0`.** Per concern 3.3. The cost of "scripts need one line of regex" is lower than the cost of "bug reports contain unlabeled version numbers." Machine-consumption has a better channel: `/api/v1/version`. Consistency with Collaboard is a bonus.

### Q3: Auto-clear `com.apple.quarantine` in `install.sh`

**Yes, but print a line saying you did it.** Per concern 3.5. Silent security-sensitive operations are worse than explicit ones. "I cleared the quarantine attribute because this binary isn't notarized" is honest; a silent `xattr -d` is magic. Magic makes operators mistrust the tool the first time something unrelated goes wrong.

### Q4: Dry-run workflow in #153 or follow-up?

**Follow-up.** Ship the real workflow first, cut v0.1.0, use the release itself as the E2E validation. If v0.1.0 goes badly, fix-forward with a patch. A `publish-dryrun.yml` has real value but it's not on the critical path to shipping, and it doubles the surface to review. Ship the smaller thing, iterate.

My softer lean than Remy's: do this as a follow-up *card*, not a follow-up *PR*. File it now so it doesn't get lost.

### Q5: Archive filename includes version?

**Yes — `collabhost-v0.1.0-linux-x64.tar.gz`.** Per concern 3.2. Strong disagreement with the "match Collaboard" rationale. The out-of-band distribution case is real: bug reports, email attachments, "can you send me the binary" conversations. Self-documenting filenames win here by a large margin. Script complexity cost is one variable substitution.

This is the place I have a stronger view than Marcus might — his architectural lens cares about script determinism; my operator lens cares about operator recognition. Both are real; my concern wins because the script is written once and the operator sees the filename every download.

### Q6: Diagnostics in `/api/v1/status` vs `/api/v1/version`?

**Both, with clean separation.** Per concern 3.6.
- `/api/v1/version` → build identity: `version`, `commit`, `platform`. "What binary is this?"
- `/api/v1/status` → runtime state: `version` (already there), `uptime`, `apps_running`, `proxy_state` (new per Q1), etc. "What's happening on this host?"

Remy's "one-field /version is pure" argument is pure at the expense of useful. The two endpoints answer different questions — let them.

---

## 5. INSTALL.md content check

### What Remy outlined (§13.1)

1. Quick start
2. What's in this archive
3. First-run admin key
4. Configuration
5. macOS: first-run quarantine
6. Updating
7. Troubleshooting (Caddy, port 443, macOS, SQLite)
8. Uninstall
9. Verifying checksums
10. Version & diagnostics

### What I'd add / restructure

| # | Section | Status | Why |
|---|---------|--------|-----|
| 1 | **Quick start** | Keep + expand | Add "open http://localhost:58400 in your browser" as step 3 |
| 2 | **Your admin key** | **Promote to top-level** | Currently buried. Move above "what's in archive" — it's the second thing after starting up |
| 3 | **What's in this archive** | Keep | |
| 4 | **Where Collabhost stores data** | **NEW** | `$HOME/.collabhost/bin/data/` by default, `COLLABHOST_DATA_PATH` override, first-run creates it |
| 5 | **Changing the API port** | **NEW** | `Proxy:SelfPort` in `appsettings.Local.json`, common enough that operators will hit it |
| 6 | Configuration | Keep | |
| 7 | macOS first-run quarantine | Keep | |
| 8 | Updating | Keep | |
| 9 | **Verifying the install** | **NEW** | `Collabhost.Api --version` + `curl http://localhost:58400/api/v1/status` — positive signals |
| 10 | Troubleshooting | Expand | Add: "first-run admin key missing from scrollback" (points at the file written per concern 3.1), "cannot reach dashboard at localhost:58400" (port in use, firewall) |
| 11 | Uninstall | Keep | |
| 12 | Verifying checksums | Keep | |

**Length target:** ~150-200 lines of markdown (Remy's estimate of 100-150 is tight once these additions land). Still short enough to read in one sitting.

### The content hierarchy rule I'd apply

INSTALL.md reader is *post-download, pre-success*. Their goal is "get from extracted archive to `I can see my dashboard in the browser`." Order content by what blocks that journey:

1. Start it (extract, run)
2. Get an admin key (before you can do anything)
3. Open the dashboard (the success signal)
4. Configure what doesn't fit defaults (port, paths, Caddy)
5. Handle platform-specific issues (macOS quarantine)
6. Update later
7. Operational reference (troubleshoot, uninstall, verify)

What Remy has is close. The restructure above reflects that hierarchy.

---

## 6. Install flow walkthrough — step by step, user's seat

Walking through the proposed flow as a fresh Windows operator. Annotated with friction points.

**Step 1.** Operator lands on `github.com/mrbildo/collabhost` via social card click-through. README install section says:

```
iwr -useb https://mrbildo.github.io/collabhost/install.ps1 | iex
```

Operator opens PowerShell. Pastes. Hits Enter. (Zero-friction path.)

**Step 2.** Script runs. What does the operator see? **Unspecified in the spec.** I'd expect:
```
Collabhost installer
Detected: win-x64
Downloading collabhost-win-x64.zip (v0.1.0) ...
Downloading checksums.txt ...
Verifying SHA256 ... OK
Extracting to C:\Users\<name>\.collabhost\bin ...
Added C:\Users\<name>\.collabhost\bin to User PATH. Open a new terminal for it to take effect.
Done.
To run: open a new terminal and type:  collabhost
```

**Friction point 1:** no spec for installer stdout messaging. The success path is one line per step but the steps aren't named. **Recommend:** spec an expected stdout template in §9. Operators who hit any friction will paste the stdout into a bug report; it needs to be self-labeling.

**Step 3.** Operator opens new terminal. Types `collabhost`. (Except the binary is `Collabhost.Api.exe`, not `collabhost`. The install script doesn't rename it and the spec doesn't specify a wrapper.) **Friction point 2:** binary name is `Collabhost.Api` (with a capital, dotted, developer-facing identifier) but the install path is `collabhost`. Operator confusion likely.

**Recommended change.** Either:
- Rename the published binary in the workflow's `dotnet publish` output to `collabhost` / `collabhost.exe` (via `AssemblyName` in csproj or post-publish rename). This is the right UX fix.
- Or: install script creates a wrapper / symlink / `doskey` alias so `collabhost` works.

Option 1 is cleaner and matches every other single-binary tool (go, deno, bun, rustc).

**Step 4.** Binary runs. Caddy starts. Dashboard is reachable. Admin key appears somewhere in stdout mid-stream. **Friction point 3:** per concern 3.1, capture is unreliable. If Caddy startup noise floods the terminal, the key scrolls off before it's copied.

**Step 5.** Operator browses to `http://localhost:58400`. **Friction point 4:** nothing in the install script *told them* this URL. They have to read INSTALL.md or the README. A one-liner in the install script's "done" output:
```
Done.
Open http://localhost:58400 in your browser.
Your admin key is in the first-run log — see C:\Users\<name>\.collabhost\bin\data\first-run-admin-key.txt
```
closes this gap.

**Step 6.** Dashboard prompts for key. Operator pastes. Authenticated. Dashboard loads.

**Step 7 (future).** Operator re-runs `install.ps1` to update. **Friction point 5 (none):** merge-safe, data preserved. This is the good part.

### Summary of walkthrough findings

| # | Friction point | Severity | Fix |
|---|---------------|----------|-----|
| 1 | Installer stdout messaging unspecified | Medium | Spec expected template in §9 |
| 2 | Binary name `Collabhost.Api` vs PATH exposure `collabhost` | **High** | Rename binary to `collabhost` in publish output |
| 3 | Admin key capture fragile on first run | **High** | Concern 3.1 — write to file |
| 4 | Installer doesn't tell operator where to browse after done | Medium | One-line "open http://localhost:58400" in stdout |
| 5 | Update flow is clean | None | Keep as-is |

---

## 7. Implications for #155

**What this spec enables for #155 (the README restructure):**

- Stable install URLs — I can write `curl | bash` / `iwr | iex` one-liners with confidence
- `--version` and `/api/v1/version` give me verification commands to put in "Check your install worked"
- INSTALL.md exists and ships in-archive, so README can point at it for anything post-install-detail
- Env var list is enumerated, so the configuration section has a canonical source
- macOS Gatekeeper is handled (mostly) inside install.sh

**What I want locked in #153's spec to make #155 smoother:**

1. **Archive filename includes version** (concern 3.2). Affects how I write the "manual download" fallback in README.
2. **Binary renamed to `collabhost` in publish output** (walkthrough point 2). If the published binary is `Collabhost.Api.exe` I'll be documenting that name in README, which is terrible UX.
3. **`--version` format as `Collabhost 0.1.0`** (concern 3.3). I'll be showing `--version` output in docs.
4. **Admin key retrieval path spec'd** (concern 3.1). Either "check stdout" (current, fragile) or "check `{DataPath}/first-run-admin-key.txt`" (proposed). I need to tell operators exactly where to look, and that location needs to be real.
5. **Installer stdout templates** (walkthrough point 1). I want to be able to show an expected-output block in README: "When you run the installer, you'll see this." If stdout format is unspecified, that block drifts.
6. **Proxy-disabled state exposed in `/api/v1/status`** (Q1). The frontend will want to show a banner; I'd rather I commit to that in #155 and know the backend flag exists.
7. **GitHub Pages landing page styled per War Machine** (concern 3.8). I'll handle the styling in #155; the spec should just commit that the minimal-HTML version is a placeholder, not the final shape.

None of these block #153. All of them are quality-of-life for #155.

**What #155 explicitly owns (not this card):**

- README rewrite (operator-first framing, install section rewrite, feature tour)
- CONTRIBUTING.md vs `docs/development.md` decision (where dev setup content migrates)
- Screenshot pass (any install-experience shots)
- Badge + link audit
- End-to-end fresh-user verification

The audience split in card #155 is well-drawn: README for users/operators, CONTRIBUTING for contributors, INSTALL for in-archive. If all three are built correctly there shouldn't be much content duplication, just different cuts of the same install story.

---

## 8. Questions for Bill

1. **First-run admin key capture UX** (concern 3.1). This is the single biggest UX hole I see. Writing the key to a file + logging to stdout costs ~15 lines of code; the product gets meaningfully more usable. Want explicit approval before Remy scopes it into the spec.

2. **Binary rename to `collabhost`** (walkthrough point 2). This is a small but meaningful publish-time change. Currently the assembly is `Collabhost.Api`; the PATH entry after install would need to match. Do we do this in #153 or defer?

3. **Archive filename versioning** (concern 3.2). Strong opinion on my side; Remy's "match Collaboard" argument treats Collaboard as a template it wasn't chosen to be. Your call.

4. **GitHub Pages landing page ownership.** I'm happy to own the War Machine styling of `docs/index.html` as part of #155. That moves scope out of #153. OK?

5. **"Proxy disabled" dashboard indicator.** If we soft-fail Caddy startup (per Q1), the frontend needs to surface that state. I'll own the frontend component; can we commit in this spec that the backend surfaces the flag in `/api/v1/status` or similar?

6. **Dry-run workflow follow-up card.** Remy flagged this. Should we file it now so it doesn't get lost?

---

## 9. Observations

### 9.1 Spec internal inconsistencies

- **§3.6 matrix uses native runners (`windows-latest`, `macos-latest`, `ubuntu-latest`)** but §1 says "inherits Collaboard's skeleton." Collaboard runs everything on `ubuntu-latest`. The spec's stance is actually a *departure* from Collaboard; the summary wording understates it. Not wrong, just imprecise.
- **§8.5 says `--version` short-circuits before `WebApplication.CreateBuilder(args)`** — correct. But §15.1's "CLI `--version` flag" test proposes `ProcessStartInfo.RedirectStandardOutput = true, read stdout, compare`. If Program.cs short-circuits, the binary never wires up any logging framework. Verify that stdout write gets flushed before the `return 0` — there's a subtle buffering risk on Windows if the first output is `Console.WriteLine` before any ASP.NET output-redirection setup. Probably fine, but one-line test to confirm.
- **§6.4.1 proposes `ProxyAppSeeder.ResolveBinaryPath` becomes priority-chain** but §5.1's archive tree still lists `appsettings.json` — which currently has `"BinaryPath": "caddy"`. §6.4.1 then says "change the default to empty string." The archive tree doesn't reflect that change. Minor, but sync it.

### 9.2 Content that will drift between INSTALL.md and README unless locked

- Install one-liners (both will have them)
- Troubleshooting (INSTALL.md is authoritative per Remy; README should link)
- Admin key retrieval (same situation)
- macOS Gatekeeper (INSTALL.md has the detail; README should link)

**Recommendation (for #155, not #153):** establish a "INSTALL.md is authoritative for post-install; README links to INSTALL.md" rule. Where READMEs duplicate, they duplicate by brief summary + link, not by copy-paste. Prevents drift.

### 9.3 Current CONTRIBUTING.md conflicts with post-release README direction

`CONTRIBUTING.md` currently contains dev setup content (Prerequisites, Setup, Build and Run with Aspire, Standalone, Testing, Linting). Card #155 calls this out as something to decide on. Observing here: **the split in card #155 is right**. The dev-setup content migrates to `docs/development.md` (or stays in CONTRIBUTING.md with the contribution-process stuff folded in), README loses dev-setup content entirely.

This is good alignment. The spec doesn't change anything in CONTRIBUTING.md, so no conflict. Just noting the shape of the handoff.

### 9.4 Stale CLAUDE.md "Known Issues" — Remy's anomaly is correct

Confirmed: card #83 is stale. The admin port is already dynamically allocated in `_Registration.cs`. Close #83 in a trivial follow-up, update CLAUDE.md's "Known Issues" list. Remy called it out in §17.3 — I concur.

### 9.5 `caddy.version` at repo root is the right call but add a comment

§6.1 proposes a plain-text `caddy.version` at repo root. Small UX suggestion: add a comment to the file header (bash-style `# Caddy pin — bumped per #154 runbook`) so anyone grepping the repo for the version has context. Yes it's "just a version number" — but a repo-root file with no context reads like a mystery to first-time readers.

### 9.6 Frontend dist artifact retention: 1 day is tight for debugging

§3.5 sets `retention-days: 1` on the frontend-dist artifact. If a matrix leg fails on day 1 and isn't re-run until day 2, the shared frontend artifact is gone. The fix is either re-run `build-frontend` (cheap), or bump retention to 3 days (trivial storage cost). Minor.

### 9.7 `caddy` binary in archive root vs. subdirectory

§5.1 ships `caddy` at the root of the archive alongside `Collabhost.Api`. An operator adding `$HOME/.collabhost/bin` to PATH now exposes *both* binaries to the shell. Typing `caddy` anywhere launches the bundled Caddy, which may surprise someone who has a system Caddy installed.

**Recommendation.** Ship `caddy` in a `bin/` subdirectory or under a name that won't collide on PATH (`caddy.bundled`? awkward). This is low-severity but worth consideration. Collaboard does not have this issue because Collaboard doesn't ship Caddy.

Or: accept the risk and note in INSTALL.md. Operators with a system Caddy can uninstall the bundle. Probably fine for v1.

### 9.8 No observability plan for release pipeline itself

The spec has thorough test coverage for *what ships*, but no mention of "how do we know a release went well in aggregate." Basic telemetry questions:
- How long does a full release take (wall clock)?
- Are any matrix legs consistently slower?
- What's the artifact size trend over time? If one release suddenly jumps 20%, that's worth noticing.

Nothing here is blocking. Noting it because we'll regret it in six months if we don't build even a tiny "release metrics" note somewhere. For #153 this can live as a follow-up.

---

*End of review. ~560 lines. Drafted without reading Marcus's parallel review per dispatch constraints.*
