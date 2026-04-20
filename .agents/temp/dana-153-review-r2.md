# Card #153 Release Pipeline Spec — Dana R2 Review

**Reviewer:** Dana (frontend lead, operator-UX + docs-alignment lens)
**Spec:** `.agents/specs/release-pipeline.md` (1668 lines, branch `spec/153-release-pipeline-r2`, commit `c0a085e`)
**Date:** 2026-04-18
**Scope:** Second-pass review. Validate Remy's revision against my R1 catches and against the #156/#158/#159 rulings. Continue raising new concerns; Bill asked for candor.

---

## 1. Verdict

**Ship-ready with targeted wording changes in §9.7 and §13.1, and a spec-level decision on Q7 (`appsettings.json` reinstall behavior).** None of the open items block merging the spec; a couple of them block shipping v0.1.0 off it. Remy landed every R1 catch cleanly and incorporated the #159 outcomes without leaving framing debt.

The two things I still want out of this spec before it goes into implementation:

1. **Resolve Q7 (`appsettings.json` reinstall) with a clearly named operator contract** — not a judgment call buried in open questions. The spec's implicit "Option A + loud warning" is the right answer, but the warning is not yet written and the INSTALL.md "Updating" section doesn't match it yet.
2. **Commit the installer stdout templates as normative, not illustrative.** §9.7 has real stdout lines in two places. Right now the section reads like worked examples; I want it to read like a contract.

Everything else is quality-of-life. The spec is better than R1 in structure and clarity. The retirement of "escape hatch" vocabulary and the clean folding of #159 into §2.5 removed an entire category of drift risk. The shift from "log fatal" to "soft-fail + `proxyState` visibility" genuinely improves the operator loop.

---

## 2. R1 concerns — status table

| # | R1 concern | R2 status | Notes |
|---|---|---|---|
| 1 | Admin key capture on first run is fragile | **Deferred correctly.** Spec names #156 (behavior) + #158 (UX) as owners and leaves a `{PLACEHOLDER}` block in INSTALL.md §13 with an explicit blocker note for v0.1.0. This is the right call given the behavioral decisions only landed yesterday. See §3 below for the partial-story opinion. |
| 2 | Archive filename must include version | **Fully addressed.** Decision 8 locked `collabhost-{version}-{rid}.{ext}`, no `v` prefix. §4 table uses the versioned form. §9.3 constructs `${VERSION}` from `${TAG#v}` cleanly. §9.7 extraction uses `$ARCHIVE_ROOT="$TMP_EXTRACT/collabhost-${VERSION}-${RID}"`. §3.6 publish step outputs match. Consistent end to end. |
| 3 | Binary rename `Collabhost.Api` → `collabhost` | **Fully addressed.** Decision 9 + §3.6 `-p:AssemblyName=collabhost` + §4 rename note + §5.2 updated. One small concern about `InternalsVisibleTo` — Remy already filed it as Q9, so it's surfaced. |
| 4 | INSTALL.md gaps | **Addressed in the outline; content deferred.** §13.1 now has the 12-section ordering I recommended, with "Verifying the install" (§13.1 #7), "Opening the dashboard" (§13.1 #3), admin key promoted to #2, env-vars reference at #12. Actual INSTALL.md is my card (#155-ish). See §5 below. |
| 5 | Installer stdout templates unspecified | **Partially addressed.** §9.7 now has the macOS xattr confirmation line as a concrete stdout template. PATH lines in §9.6 are concrete. But the full installer transcript (section 2 of my R1 walkthrough) is still not specified as a contract. See §6 below. |

---

## 3. Remy's three explicit asks

### 3.1 INSTALL.md §13.1 section ordering — validation

**Validated with two small corrections.**

The ordering now matches what I asked for in R1: quick start → admin key → opening the dashboard → what's in the archive → configuration → macOS → verifying → updating → troubleshooting → uninstall → checksums → env var reference.

Two issues with the R2 text:

**(a) "Opening the dashboard" at #3 is good, but "Verifying the install" at #7 is in the wrong place.** In my R1 I put verification at #9 (after configuration and macOS, before updating). Remy has it at #7, between macOS and updating. That's actually *better* than where I'd put it — verification belongs tight against the success signals an operator just achieved, not buried after configuration. Leave it at #7.

But consider: the quick-start (§13.1 #1) already tells them to open the dashboard. Section #3 "Opening the dashboard" is now doing the work of "here's what success looks like." That's fine, but let's name it honestly. Suggest renaming §13.1 #3 from "Opening the dashboard" to **"What success looks like"** or **"Expected first-run behavior"** — it's the "you should see X, Y, Z; if you don't, jump to troubleshooting" section, not just a URL.

**(b) The `{PLACEHOLDER}` block in the Admin key section needs an explicit boundary.** §13.3 calls it a v0.1.0 blocker, which is correct. But the spec doesn't say what the placeholder *says* to a reader who extracts the archive before the cards land. If #156 slips past v0.1.0 for any reason, INSTALL.md either ships with `{PLACEHOLDER}` visible (unacceptable) or ships with *some* text that's best-effort against the 3-scenario model from #156's comment. Commit to the latter. See §4 below — I think there's a partial story I can write now.

### 3.2 §9.7 installer stdout confirmation lines — validation

**The lines that are there are good. The ones that aren't there are the problem.**

What §9.7 currently specifies as stdout:

- `"Cleared macOS quarantine attribute on collabhost and caddy."` — §9.7 install.sh block.
- `"Added Collabhost to PATH in $RC_FILE"` / `"Open a new terminal or run: source $RC_FILE"` — §9.6.
- `"Added Collabhost to User PATH. Open a new terminal for it to take effect."` — §9.6 PowerShell.

These are operator-readable and self-explanatory. Good.

**What's still missing, in order of operator journey:**

1. **Greeting / platform detection line.** The operator pastes `iwr | iex` and the first thing they should see is something like `Collabhost installer — detected win-x64`. Right now the script starts and nothing tells them the installer recognized their platform. If arch detection fails (§9.2's `*)` case or the PowerShell `throw`), they do get a clear error — but the happy path is silent until the PATH line.

2. **Download progress / naming.** When `curl -fsSL` runs, it runs silently (because `-s`). Operator sees nothing until it completes. For a ~55MB download that's 5-20 seconds of "is this hung?" For an operator on a slow connection it's 60+ seconds. **Recommend:** print `Downloading collabhost-0.1.0-win-x64.zip (54 MB)...` before the download, and `OK` after. Two lines, no spinner complexity. Or drop `-s` so curl's own progress shows.

3. **Checksum verification success line.** §9.5 prints on mismatch. It does not print on match. **Recommend:** print `Verifying SHA256... OK` on success too. The most reassuring moment in any install is the "your binary is the one we shipped" confirmation. It's the reason checksum verification exists.

4. **Extraction + install location line.** After extract completes: `Installed to $HOME/.collabhost/bin.`

5. **The "now what" line.** After the PATH line, an operator should see: `Open a new terminal and run: collabhost` followed by `Then open http://localhost:58400 in your browser.` Right now §9.6 tells them to open a new terminal but doesn't tell them what to run, and nothing tells them the URL.

A proposed full transcript for the success path (Linux/macOS), to be added to §9 as a **normative** expected-output block:

```
Collabhost installer
Detected platform: linux-x64
Downloading collabhost-0.1.0-linux-x64.tar.gz (54 MB)...
Downloading checksums.txt...
Verifying SHA256... OK
Extracting to /home/operator/.collabhost/bin...
Added Collabhost to PATH in /home/operator/.bashrc
Open a new terminal, then run:
    collabhost
Once running, open http://localhost:58400 in your browser.
```

And for macOS, one extra line before "Added Collabhost to PATH":
```
Cleared macOS quarantine attribute on collabhost and caddy.
```

Committing this as a template does three things:
- Operators who hit a problem paste a recognizable transcript into a bug report.
- #155 README can show "here's what you'll see" without speculation.
- Install script authors have a contract to implement against.

**Recommendation:** Add §9.9 "Installer stdout contract" with the block above as normative. Reference it from the "Verifying the install" section of INSTALL.md.

### 3.3 Q6 `proxyState` wire shape during startup — my call

**Add `"starting"` as a fifth enum value. Include it in the contract.**

Values:
- `"starting"` — probe is running or not yet run. Expected only during the 5-second post-launch window.
- `"running"` — Caddy is up, admin API reachable, routes syncing.
- `"failed"` — launched but probe failed within the budget. The most operationally actionable state.
- `"disabled"` — no Caddy binary resolved (env var unset, config unset, bundled absent).
- `"stopped"` — operator explicitly stopped proxy via UI / API.

**Rationale from the dashboard perspective.** A three-state widget (good / bad / unknown) with an implicit fourth "loading" state via `null` or absence is harder to render cleanly than a four-state enum with an explicit `"starting"`. The dashboard will poll `/status` at 3s intervals (per `POLL_INTERVALS.active` in frontend `constants.ts`). During the first 5 seconds of startup, two polls will hit before the probe resolves. I'd rather they return `"starting"` than `null` — an explicit "we're trying, ask again soon" reads cleanly in the UI without a client-side `?? "unknown"` dance.

`null` is also a contract-smell. Every other field in `/status` returns a value; giving `proxyState` a nullable shape for a purely transient condition means every consumer (dashboard, CLI scripts, MCP tools) has to branch. An explicit `"starting"` is the same cost in backend code and zero cost on consumers.

**One edge case worth naming.** If `VerifyCaddyReady` hasn't been invoked yet (e.g., the supervisor hasn't promoted Caddy to Running), what's the state? I'd say **`"starting"` until proven otherwise.** The initial `CurrentState` should default to `"starting"`, transition to one of the resolved states as the probe completes, and stay there.

**Dashboard surface (my forward commitment).** When `proxyState` is `"failed"`, the `StatusStrip` shows a persistent indicator with one-click access to a short remediation: "Caddy did not respond within 5s. Check logs or try setting `COLLABHOST_CADDY_PATH`." When `"disabled"`, slightly softer wording: "Proxy subsystem is not available. Bundled Caddy binary could not be located." When `"starting"`, a neutral indicator — no alarm, it resolves in seconds. When `"stopped"`, a dim-but-visible state matching the proxy app's stopped status in the app list. I'll propose actual designs in a frontend card; this spec just needs to commit the enum and the field.

**One contract-level ask.** Make `proxyState` a top-level field on the `/status` response, not nested under a `proxy` object. The `SystemStatus` record today is flat (version, uptime, app counts) and `proxyState` reads better as a sibling than as `.proxy.state`. Matches the precedent.

---

## 4. Admin key INSTALL.md — partial story against the #156 model

The #156 comment from Nolan (2026-04-18) locked the 3-scenario behavioral model:

1. **Blind first run, no admin key configured** — app generates ULID, seeds DB, emits to stdout.
2. **Configured first run** — admin key provided via `appsettings.json` (or CLI arg, TBD).
3. **Configured key on subsequent boot** — if the key's user doesn't exist in DB, create as admin (override path).

That is enough to write a v0.1.0-shippable INSTALL.md admin key section *today*, before #158 finishes research. The stdout UX might get tweaked (banner format, phrasing, recovery path), but the contract shape is locked. Draft below, to be folded into INSTALL.md #2 with only the phrasing of the emit line pending #158:

```markdown
## Your admin key

Collabhost needs an admin key to authenticate you the first time you open the
dashboard. You have three options, in order of increasing control:

### Option 1 — Blind first run (the default)

If you run `collabhost` without setting an admin key, Collabhost generates one
and prints it to stdout on first start. It looks like this (exact format pending):

    {PLACEHOLDER: first-run admin key emit format — see release notes for 0.1.0}

Copy the key immediately. If you miss it, see "Recovery" below.

### Option 2 — Configured first run

Set `Auth:AdminKey` in `appsettings.json` before the first run. Collabhost
uses your value instead of generating one:

    {
      "Auth": {
        "AdminKey": "01HXYZ..."
      }
    }

### Option 3 — Change the admin key later

Edit `Auth:AdminKey` in `appsettings.json` and restart Collabhost. On the next
boot, if the key you provide is not already associated with a user, Collabhost
creates a new admin user with that key. (The old key and user are not removed —
you'll want to clean those up via the dashboard.)

### Recovery

{PLACEHOLDER: if stdout was missed — pending #158 research}
```

That's the partial story. Two placeholders, both narrow, both surfaced. **The PLACEHOLDER boundaries are small enough that if #158 slips past v0.1.0, we can ship INSTALL.md with them written out verbatim and a release-notes pointer.**

**Recommendation for the spec.** §13.3 currently says "Admin key wording deferred to #156/#158 with explicit placeholder blocker for v0.1.0 release." Soften that to: "Admin key **structural content** is specified here (the 3-scenario model is locked). Two narrow phrasing placeholders (first-run emit format + recovery path) remain pending #158, but are not release blockers — INSTALL.md can ship with them marked `see release notes` if #158 slips." This moves the admin key section from "blocker" to "content-complete with two phrasing TODOs."

I can write this draft against #156's locked model and hand it to #155 implementation without waiting on #158. Flagging that so we don't treat #158 as more blocking than it is.

---

## 5. New R2 concerns (ranked)

### 5.1 Q7 `appsettings.json` reinstall — spec takes a position, not an open question [HIGH]

This is the biggest R2 gap. §9.7 describes the behavior (overwrite) and the tension (operator edits lost vs. new defaults needed). §17.2 Q7 asks Bill for sign-off. That's a false choice.

**The right answer is Option A + loud warning + INSTALL.md documents it up front — and that choice is strong enough that it shouldn't sit in the open-questions bucket.** Reasons:

- The single-file config model (§2.5) only works if `appsettings.json` can evolve shipped defaults. Option B freezes defaults the moment an operator touches the file — at which point they never get future improvements (new keys, better defaults) without a manual diff pass.
- Env vars are the *blessed* override mechanism per #159. They survive reinstall by construction. Any operator who's customized behavior should be using env vars anyway. Treating `appsettings.json` as "operator's file" creates a second mutable surface that violates §2.5's "one source of truth" framing.
- "Silent overwrite" is the bad version. "Overwrite with a stdout line saying we did" + "INSTALL.md's 'Updating' section says so" is the good version.

**Recommendation.**

Move Q7 out of open questions. In §9.7:

1. Add a stdout line during the install: `Updating appsettings.json (shipped defaults). Operator customizations via env vars are preserved; edits to this file are not.` Only emit this line if the file already existed — first-run doesn't need it.

2. In INSTALL.md §13.1 #8 "Updating", add a short paragraph:
   > The installer overwrites `appsettings.json` with the shipped defaults on every run. Environment variables (see "Environment variables reference") are preserved — they are the supported way to customize settings. If you need to edit `appsettings.json` and keep your edits across updates, either use env vars instead, or maintain your own copy outside `$HOME/.collabhost/bin/`.

3. In §2.5 add one sentence: "Because `appsettings.json` is overwritten on reinstall (§9.7), env vars are the preferred operator override for anything that needs to survive upgrades."

This closes the operator contract. Bill should still sign off, but it doesn't need to sit in open-questions.

### 5.2 Q3 log directory for pre-crash diagnostics — in #153 scope [HIGH]

My R1 friction-point 5 argued that if the binary crashes before the operator captures stdout, there's no post-mortem path. That's still true. The spec asks: in #153 or follow-up?

**In #153. Scope it as part of Phase 4.**

Why:

- Without it, "how do I diagnose a crashed Collabhost on v0.1.0" has no answer. That is an operator-facing gap in v0.1.0, not a polish item.
- The cost is small: ~30 lines to tee stdout/stderr to `{DataPath}/logs/collabhost-{timestamp}.log` with retention-by-count (keep last 5).
- The alternative — "file a follow-up card" — creates a v0.1.0-shipped product that cannot tell an operator what failed when it failed. That is a worse first-release experience than the absence of Docker support (which also isn't in v1 but isn't operator-stranding).
- It shapes what INSTALL.md can say in §13.1 #9 "Troubleshooting" for the "Binary crashes before I see anything" row. Right now that row points at `data/logs/` per Q8. Without Q3 resolved, the row has no content.

**Recommended change.** Promote Q3 to a spec requirement in §12 (Configuration resolution) or §15 (Test Strategy). The log directory is `{DataPath}/logs/`, filename is `collabhost-{YYYY-MM-DDTHH-MM-SS}.log`, retention is last 5 files, format is "stdout and stderr interleaved with timestamp + stream label." Testable via integration test that forces a startup failure and asserts the log file exists + contains the error.

**Lower-cost alternative if scope is tight.** Ship v0.1.0 with a compile-time-fixed path (not configurable) and test-gate only the file-creation part. We can add rotation and configurability in v0.2.0. But ship *something* — "crashed with no trace" is a bad first-release story.

### 5.3 §9.7 installer transcript is illustrative, not normative [MEDIUM]

See §3.2 above. The spec's examples in §9.5, §9.6, §9.7 each have real stdout lines, but they're scattered and read like "what the script happens to print" rather than "what the contract says the script prints." The proposed §9.9 normalizes this.

### 5.4 `appsettings.json` gets shipped but is not in the env-var preferred override path — operator trap [MEDIUM]

§12.3 ships exactly three env vars: `COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_CADDY_PATH`. §12.3 "What's deliberately not in the list" explicitly excludes proxy-specific knobs (`BaseDomain`, `ListenAddress`, `SelfPort`, `CertLifetime`), deferring to §17.2 Q4 for a Bill decision.

This creates an operator trap: the only way to change `Proxy:ListenAddress` (to something other than `:443`, which collides with anything else using 443) is to edit `appsettings.json` — which the installer then overwrites on update. And env vars for those settings are "not in v1."

**The trap resolves one of three ways:**

1. Add env vars for at least `Proxy:SelfPort` and `Proxy:ListenAddress` in v1. (Covers the two changes-on-port-conflict scenarios.)
2. Spec an escape hatch: a sibling file `appsettings.Operator.json` that the installer never touches, loaded after `appsettings.json`. (Reintroduces multi-file complexity we just eliminated.)
3. Accept that port-collision operators will lose their `ListenAddress` edit on every update until they either change to a different port permanently in env vars or stop updating.

**My recommendation:** Option 1, narrowly scoped. Ship `COLLABHOST_PROXY_LISTEN_ADDRESS` and `COLLABHOST_PROXY_SELF_PORT` in v1. These are the two settings an operator is most likely to hit when their first install fails ("port 443 already in use" is one of the most commonly anticipated errors — §13.1 troubleshooting lists it). Everything else (`BaseDomain`, `CertLifetime`) can stay appsettings-only.

Two env vars, two lines of code each (read env → override config). Closes the trap.

### 5.5 `caddy` binary on PATH collides with system Caddy [MEDIUM]

Flagged in R1 §9.7, not addressed in R2. The install script adds `$HOME/.collabhost/bin` to PATH, which exposes `caddy` / `caddy.exe` to the shell. An operator with a system Caddy install now has two caddies: `which caddy` resolves to whichever is first on PATH. That's almost always the collabhost bundled one, because `$HOME/.collabhost/bin` gets prepended.

From an operator's perspective: they type `caddy version` to check their system Caddy and get the collabhost-bundled pin. Quietly surprising.

**Recommendation.** One of:

1. **Ship Caddy under a disambiguated name.** `caddy-collabhost` or similar. Collabhost's resolver (§6.4.1 bundled fallback) looks for the disambiguated name; nothing else on the user's system does. Clean. Costs one `mv` in the workflow and one filename in `CaddyResolver.cs`.

2. **Ship Caddy in a subdirectory not added to PATH.** `$HOME/.collabhost/bin/collabhost` on PATH; `$HOME/.collabhost/sidecar/caddy` not on PATH. Clean, but means the bundled-sidecar resolver looks at `AppContext.BaseDirectory/../sidecar/caddy` which is uglier than the current `AppContext.BaseDirectory/caddy`.

3. **Accept the collision, document it.** Add an INSTALL.md note in the troubleshooting section: "If you use Caddy outside Collabhost, the installer puts the bundled Caddy on PATH — it will shadow any system Caddy. To use your system Caddy, either reorder PATH or invoke it by absolute path."

Option 1 is the UX-correct answer. Option 3 is a cop-out but shippable. Option 2 is tempting but fiddles with the resolver unnecessarily. My vote: Option 1, implemented during Phase 4.

### 5.6 `/api/v1/status` shape change needs a consumer-compat line [LOW-MEDIUM]

§14.4 says "The `SystemStatus` response shape gains one new field in R2 — `proxyState`." But the existing consumer list (dashboard, MCP `get_system_status`, any external script) already has a shape contract.

**Spec gap:** nothing calls out that `proxyState` should be *additive* (new field on an existing response), not replacing or renaming anything. If a future PR accidentally renames the `version` field or removes `uptime`, older MCP clients (including ones installed by agents that ship with a specific version of the MCP tool) would break silently.

**Recommendation.** Add a one-line note in §14.4: "Additive-only change: `proxyState` is a new field on `SystemStatus`; no existing fields renamed or removed. Consumers reading only `version` / `uptime` / etc. continue to work unmodified."

Small thing. But I've seen API contracts erode one "additive" change at a time, and the discipline of naming it matters.

### 5.7 `/api/v1/version` explicitly does NOT gain platform/commit fields (R1 concern 3.6 revisited) [LOW]

My R1 concern 3.6 argued for `{ version, commit, platform }` on `/version`. Bill/Remy closed Q6 with `"/version = version+commit+platform, /status = runtime state"` — which matches my R1 ask.

But §8.4 of the R2 spec still shows the one-field response:

```json
{ "version": "0.1.0" }
```

And explicitly says: "No extra fields in v1 -- resist the urge to stuff in commit hash, build date, etc."

§17.3 closes Q6 with "`/version` = version+commit+platform." The spec body and the closed-question table disagree.

**This looks like a merge glitch — R1 §8.4 didn't get updated when the question was closed.** Easy fix.

**Recommended change.** Update §8.4 to match the Q6 resolution:

```json
{
  "version": "0.1.0",
  "commit": "abc1234",
  "platform": "linux-x64"
}
```

And remove the "resist the urge" sentence.

Alternatively, if the spec wants to ship with the minimal shape and promote commit/platform to a v0.2.0 additive, fine — but then §17.3 Q6 should be re-opened or reframed. Pick one.

### 5.8 Scripts use `mrbildo/collabhost` — spelling of the GitHub org [LOW]

§9.3 URL template:

```
https://api.github.com/repos/mrbildo/collabhost/releases/latest
```

§10.1 repo settings use `mrbildo.github.io`.

Both of these assume the GitHub org/user is `mrbildo`. The actual owner (from `git remote get-url origin`) I don't have in front of me, but a simple check: this is one `find-replace` mistake away from an install script that 404s. Mention as a Phase 5 pre-merge checklist item.

(If the owner is actually `mrbildo`, ignore.)

### 5.9 `COLLABHOST_DATA_PATH` shapes data directory, connection string shapes DB path — one config, two responsibilities [LOW]

§12.3 says `COLLABHOST_DATA_PATH` overrides "Effective parent directory for SQLite DB (connection string derived from this)."

But `ConnectionStrings:Host` in `appsettings.json` is a full SQLite connection string, not a directory. An operator reading §12 learns `COLLABHOST_DATA_PATH=/var/lib/collabhost` sets the data directory. But if they then edit `ConnectionStrings:Host` directly, they're editing a connection string that has implicit structure (path extracted by `configuration.GetConnectionString`).

Concretely: what happens if an operator sets both `COLLABHOST_DATA_PATH=/var/lib/collabhost` *and* `ConnectionStrings:Host="Data Source=/elsewhere/db.sqlite"`? Per §12.4 precedence, env wins — so `DATA_PATH` takes precedence and the connection string is ignored.

That's the right answer, but it's not documented. An operator trying to debug "why isn't my connection string being honored" needs §12.4 to say: "when `COLLABHOST_DATA_PATH` is set, the full connection string is reconstructed from it; `ConnectionStrings:Host` is ignored."

**Recommendation.** Add two sentences to §12.3's `COLLABHOST_DATA_PATH` row or to §12.4:

> When `COLLABHOST_DATA_PATH` is set, Collabhost constructs the SQLite connection string as `Data Source={DATA_PATH}/collabhost.db`. Any `ConnectionStrings:Host` value in `appsettings.json` is ignored in this case.

Clarifies intent. No code change.

---

## 6. Operator first-run walkthrough — R2 pass

Walking through the proposed flow again, as a fresh Linux operator on v0.1.0.

**Step 1.** Operator lands on README, pastes the one-liner:

```bash
curl -fsSL https://mrbildo.github.io/collabhost/install.sh | bash
```

**Step 2.** Script runs. R2 stdout walkthrough (based on current §9.5/§9.6/§9.7 text):

```
(no platform detection line — SILENT)
(no download progress — SILENT for ~10-20s)
(no checksum OK line — SILENT on success)
Added Collabhost to PATH in /home/operator/.bashrc
Open a new terminal or run: source /home/operator/.bashrc
```

**Friction.** Three silent seconds blocks between script start and first visible output. Operator doesn't know if the script is hung, downloading, or just cautious. See §5.3 — §9.9 normative transcript fixes this.

**Step 3.** Operator opens new terminal, types `collabhost`. (R2 has binary renamed — good.) Collabhost starts. Caddy starts. Admin key prints to stdout. Dashboard ready.

**Friction.** "What URL do I open?" is still not answered by the script. It's in INSTALL.md, but that file is inside the archive, and the operator hasn't been directed to it. §13.1 #1 "Quick start" says "open http://localhost:58400" — good — but the installer doesn't. Recommend §9.9 transcript ends with:

```
Once running, open http://localhost:58400 in your browser.
```

**Step 4.** Operator captures admin key. Whether this works well depends on #156/#158 resolution. Current (placeholder) status: stdout emit, format TBD, recovery TBD. R2's approach of deferring this is right — but see §4 above for the partial story.

**Step 5.** Dashboard loads. Operator pastes key. Authenticated. Success.

**Step 6 (update scenario).** Three months later, re-runs install. If they've edited `appsettings.json`, those edits are silently lost. R2 doesn't surface this (§5.1 covers it).

**Summary of R2 walkthrough findings:**

| # | Friction point | Severity | R2 state |
|---|---|---|---|
| W1 | Installer stdout silence (first 10-20s) | Medium | Not fixed. Needs §9.9 normative transcript. |
| W2 | Installer doesn't tell operator what URL to open | Medium | Not fixed. One-line fix in §9.9 transcript. |
| W3 | Admin key recovery path | High | Deferred to #156/#158. Partial story writable against locked #156 model (see §4). |
| W4 | `appsettings.json` silent overwrite on update | Medium | Q7 open. Recommend closing per §5.1. |
| W5 | `caddy` binary PATH collision with system Caddy | Medium | Not addressed. §5.5. |
| W6 | Binary renamed to `collabhost` | None | Fixed. |
| W7 | Filename versioning | None | Fixed. |

---

## 7. Spec internal consistency (R2 pass)

Spotted during the read:

- **§8.4 vs §17.3 Q6 contradict on `/api/v1/version` shape.** See §5.7.
- **§12.3 says `COLLABHOST_CADDY_PATH` overrides `Proxy:BinaryPath` at precedence 1**, which matches §6.4.1. Consistent.
- **§2.5 says production uses single `appsettings.json`, no `.Local.json`.** §6.4.1 note then says "Dev-time developers who rely on PATH-resolved Caddy set `Proxy:BinaryPath = "caddy"` in `appsettings.Local.json`." Consistent — `.Local` is dev-only per §2.5, so that line is about dev, not production. Good.
- **§5.1 archive tree lists `appsettings.json`.** §5.4 lists `.Local.json` and `.Development.json` as explicitly NOT in the archive, and `appsettings.Production.json` as nonexistent. Consistent.
- **§17.4 "codebase anomalies" section is well-maintained.** Card #83 still stale (to be closed), `Platform:ToolsDirectory` dead config still tracked, `CaddyClient` blanket catch narrowed in §6.5 affected-files list. Good housekeeping.
- **§15 test strategy.** `ProxyManagerVerifyCaddyReadyTests` covers the probe. `SystemStatus` tests cover `proxyState`. If I add `"starting"` per §3.3 above, one test needs to be added: `GetStatus_ProxyStartingNotYetProbed_ReturnsStarting`.
- **§18 phase plan.** Phase 3 before Phase 2 per Marcus, fine. Phase 4 gated on #156 landing, clearly stated. Phase 5 gated on Phase 4, clear. No issues.

---

## 8. Token-level nits (trivial, filing for completeness)

- **§3.4 regex is `^v[0-9]+\.[0-9]+\.[0-9]+$`.** Future-proof note in §8 mentions this can relax to match pre-releases. Fine. No change.
- **§5.3 total archive size estimate is 50-65 MB (.tar.gz).** With the R2 rename and Caddy bundled. Accurate. README should show this number in the install section so operators know what they're downloading.
- **§6.1 recommends Caddy v2.11.2.** Confirm at implementation time that this is still current. Low risk.
- **§9.4 "intentionally omitted for v1: `--quality`, `--force`".** Also intentionally omitted but worth filing: `--uninstall`. R2 §9.8 says no uninstall script; operators manually `rm -rf`. Fine for v1, file as a follow-up card when operator friction surfaces.
- **§13.1 #7 "Verifying the install"** should include a dashboard-open check (`curl http://localhost:58400/` returns 200, or "open in browser, see login prompt"). The two verifications listed (`--version`, `/status`) don't exercise the frontend path. If the static files aren't embedded correctly, `--version` passes and `/status` returns valid JSON but the dashboard itself 404s. Adding a third verification — "open the URL in your browser, see the Collabhost login page" — closes this gap.

---

## 9. Anomalies worth filing

Nothing new beyond what's already in §17.4. The standing list is well-maintained. No new codebase issues spotted during this spec pass.

One process anomaly: the #153 → #156 dependency is clearly called out but #156's spec (`.agents/specs/production-startup.md`, per #156 acceptance criteria) hasn't been drafted yet. If #156 design work slips, #153 Phase 4 slips. Surfacing so Bill can plan sequencing.

---

## 10. Summary of asks for Remy (R3 if needed, or fold into implementation)

**Must-fix before Phase 4 branches:**

1. Close Q7 per §5.1 (spec takes the Option A + warning position).
2. Add §9.9 normative installer stdout transcript per §3.2.
3. Resolve §8.4 vs §17.3 Q6 contradiction on `/version` shape per §5.7.
4. Commit Q6 `proxyState` enum to include `"starting"` per §3.3.
5. Decide Q3 log directory per §5.2 (my vote: in scope, ship with v0.1.0).

**Should-fix before Phase 5:**

6. §5.4 env vars for `Proxy:SelfPort` + `Proxy:ListenAddress` (closes port-collision trap).
7. §5.5 disambiguate bundled `caddy` name or subdir.
8. §13.1 rename "Opening the dashboard" → "What success looks like."
9. Add browser-load verification to §13.1 #7.

**Nice-to-have:**

10. §5.6 additive-only contract line on `/status`.
11. §5.9 document `COLLABHOST_DATA_PATH` + connection-string precedence.
12. §5.8 verify GitHub org name before merging Phase 5.

**For Bill:**

- Q7 sign-off on Option A (§5.1).
- Q3 decision on log directory scope (§5.2).
- Q4 decision on proxy env vars (§5.4).

---

## 11. Questions for Bill

1. **Q7 `appsettings.json` reinstall.** Do you agree with Option A + loud warning? The spec treats this as open; I think it's closeable.

2. **Q3 log directory.** My vote: in #153 scope, ship in v0.1.0. Yours?

3. **Q4 proxy env vars.** Specifically `Proxy:SelfPort` and `Proxy:ListenAddress` — my argument (§5.4) is that port-collision is the most common first-install failure mode and operators need an env-var-shaped way to work around it that survives reinstall. Ship in v1?

4. **`caddy` PATH collision.** §5.5. My recommendation: disambiguate the bundled binary's name to `caddy-collabhost` or similar. Costs a rename step in the workflow and a filename in `CaddyResolver.cs`. OK?

5. **`/version` endpoint shape.** §8.4 shows one-field, §17.3 Q6 promises three-field. Which is right? (My R1 argument was three; your Q6 ruling matched that; §8.4 didn't update.)

6. **Admin key partial story.** I can write the 3-scenario INSTALL.md section against #156's locked model *now*, with two narrow placeholders for #158 research. Want me to include it in the INSTALL.md work I own, instead of blocking that work on #158 completing? (Soft recommendation: yes.)

---

## 12. Implications for #155

No new ones beyond R1 §7. The R2 changes make #155 easier:

- `proxyState` enum is the dashboard contract I need. I can render it without more spec work.
- Binary rename + versioned filenames mean I can write stable docs.
- INSTALL.md outline is close to shippable; I can draft against the structural content even before #158 resolves.
- The env-var list is scoped and documentable (pending §5.4).

The #155 README will link to INSTALL.md inside the archive for post-install content, with brief summaries + links to the GitHub-hosted INSTALL.md for before-download preview.

---

*End of review. ~1000 lines. Written against `spec/153-release-pipeline-r2` commit `c0a085e`. Token budget: under envelope.*
