# Marcus Review -- Release Pipeline Spec (#153)

**Reviewer:** Marcus (architecture advisor)
**Date:** 2026-04-17
**Spec under review:** `.agents/specs/release-pipeline.md` (branch `spec/153-release-pipeline`, 1438 lines)
**Review branch:** `review/153-marcus` (from origin/main)

---

## 1. Summary Verdict

**Ship this spec with targeted changes.** Remy has delivered a thorough, evidence-backed proposal that gets the big decisions right. The workflow shape is sound, the Caddy bundling model is well-grounded in prior art, and the Aspire-style install flow is the correct fit for the audience. The phasing is intelligent.

My concerns cluster around three areas: (1) the production migration gate removal is a bigger change than the spec frames it -- there's more inside that `IsDevelopment()` block than `ProxyAppSeeder`, and the spec silently inherits Collaboard's "migrate-on-startup" pattern without absorbing Collaboard's backup discipline; (2) the soft-fail-on-probe posture is the right instinct, but the spec has not reckoned with the observability and UI consequences of a Collabhost instance running with the proxy subsystem disabled; (3) "existing Caddy detection" is a novel pattern and the spec underestimates how much of a footgun it is. These are all addressable without reopening locked decisions.

On the six open questions I land mostly with Remy but break from him on two: I want the `--version` format to match Collaboard (`Collabhost X.Y.Z`), and I want diagnostics kept out of `/api/v1/version` entirely (he already proposes this). Full reasoning in section 4.

**Recommendation:** Green-light Phase 1 and Phase 3 as written. Adjust Phase 2 based on concerns C1-C3 below. Proceed to Phase 4 only after Phase 2 settles. Card #83 is confirmed fixed -- close it.

---

## 2. Strengths (preserve these)

**S1. Evidence discipline.** Every big call lands on a research artifact. The bundled-deps research did real work; the install-patterns research did real work. Remy's spec reads like someone who read the evidence and formed a view, not like someone who rationalized a predetermined answer. That's the bar.

**S2. Version helper consolidation.** Centralizing three duplicate reads into `Platform/VersionInfo` is exactly right -- it's the cheapest step with the highest leverage. The `Lazy<string>` + pure `StripCommitHash` split is the clean shape. I'd sign off on Phase 1 as written today.

**S3. Phase ordering by isolation.** Remy structured the phases so the smallest, lowest-risk change ships first and unblocks the rest. Version helper is verifiable in isolation; the escape-hatch reshape is a contained subsystem refactor; the env var floor is additive; the workflow consumes everything above it. Anyone trying to ship this in one PR would drown it in review ceremony. Five PRs sized right.

**S4. License compliance done properly.** `LICENSES/caddy-LICENSE` + `LICENSES/caddy-NOTICE` is the right posture. Not sloppy, not over-engineered. Matches the research findings.

**S5. Refusal to relax the tag regex.** Spec enforces `^v\d+\.\d+\.\d+$` and deliberately does not accommodate pre-release tags. That's scope discipline -- locked decision 5 says no pre-releases in v1, and the implementation enforces it mechanically rather than leaving a soft policy. Good.

**S6. `--clobber` + `fail-fast: false`.** Both are small choices but they make the workflow operable when one leg transiently fails. Remy understood this is an operational tool, not just a build.

**S7. Aggregate `checksums.txt` separate from per-archive sha256 files.** Correct. Install scripts want one file; manual operators want per-archive sidecars. Shipping both costs nothing.

**S8. Env var floor scoped correctly.** Spec does not try to do #104's job. It commits to "env vars take precedence, names fixed, five specific paths" and stops. This leaves #104 with room to layer UI and validation on top without forcing a renaming pass.

---

## 3. Concerns (ranked by severity)

### C1. Production migration gate removal is larger than the spec acknowledges (HIGH)

**What's wrong.** Section 6.5 and Phase 2 say to "move the `ProxyAppSeeder.SeedAsync` call out of the `IsDevelopment()` block in `Program.cs`." But looking at `Program.cs` lines 70-88, that `IsDevelopment()` block contains *three* things that are all currently production-gated:

1. `await context.Database.MigrateAsync()` -- EF Core migrations
2. `typeStore.LoadAsync()` + `typeStore.StartWatching()` -- type loading and file-system watcher
3. `proxySeeder.SeedAsync()` -- the proxy app seed

The spec mentions only item 3. Items 1 and 2 are load-bearing and have to move too, otherwise a production install won't have a database schema or any app types loaded. That's not a "small softening" -- that's a foundational behavior change that every production install inherits.

**Why it matters.** The release pipeline's entire premise is that an operator runs `Collabhost.Api` in production for the first time and it just works. Without migrations in production, an operator will hit "no such table" on the first request. Without TypeStore load in production, the app registry is empty. Remy's spec has the right instinct but has described only one-third of the required change.

**Secondary concern: migration safety.** Collaboard's recon (section 6) explicitly notes that Collaboard backs up the SQLite file as `{db}.bak-{timestamp}` before applying pending migrations. Collabhost's current code does not do this, and the spec does not propose adding it. When we remove the dev-only gate, we're committing to migrate-on-startup as the production model -- and we should adopt the backup discipline that makes that safe. The cost is ~10 lines in `DataRegistration` or a startup service; the value is that a botched migration doesn't corrupt a production database irrecoverably.

**Recommended change.**

- Rewrite §6.5 to list all three items leaving the `IsDevelopment()` block, not just the proxy seeder.
- Add a new code seam: `Data/_Registration.cs` or a new `DatabaseStartupService` that runs migrations with a pre-migration backup.
- Add to Phase 2 (or carve out a Phase 2a): the "production startup posture" work -- migration + backup + typestore load + proxy seed all moving out of the dev gate together, as one coherent change.
- Risks section should name "migration failure on production upgrade" as a first-class risk with the backup mitigation.

### C2. Soft-fail on Caddy probe needs a visibility story (HIGH)

**What's wrong.** The spec proposes soft-fail: if the post-launch probe times out, log fatal, disable the proxy subsystem for this boot, API keeps running. I agree with the *direction* -- killing the API when Caddy fails throws away the dashboard, the registry, the MCP surface, and the logs, all of which are exactly what an operator needs to diagnose the Caddy failure. Hard-fail is too aggressive.

But "API keeps running" does not mean "system is in a useful state." It means the system is in a *partially useful* state, and the user needs to know that at a glance. Today, when the proxy subsystem silently declines to seed because the binary wasn't found, the dashboard shows nothing about it. The proxy app simply doesn't exist in the registry and the operator has to notice by absence. For a shipped release, that posture is unacceptable -- operators landing on a fresh installation need an unambiguous signal that "the proxy isn't working, here's why, here's what to try."

**Why it matters.** Soft-fail without a visibility story is silent degradation. That's worse than hard-fail because the system looks healthy from outside and only the proxy features don't work. Silent degradation is the #1 source of "why isn't Collabhost routing my traffic?" support friction.

**Recommended change.** Before Phase 2 ships, the spec must commit to:

1. **System-level proxy health signal.** Extend `/api/v1/status` (or `/api/v1/system/status`, whatever the convention is) to include a top-level `proxyState` field with values like `"running"`, `"external"`, `"failed"`, `"disabled"`. The frontend Dashboard already has a `StatusStrip` and can surface this with zero new chrome.
2. **Log line that says "Collabhost is running in degraded mode."** Not just a logger.Fatal on the probe failure -- an ongoing banner-equivalent in the startup log summary that makes it obvious in any log scan. This is the "clear message" decision 10 asks for.
3. **The `proxy` app entity state.** When probe fails, the proxy app's status should render as something other than "stopped." "Failed to start" with the log link visible. That's UX polish but it's cheap: the supervisor already knows the process crashed or didn't respond.

This is also why my answer on open question 1 breaks from Remy's -- I want soft-fail *with visibility*, not soft-fail as silent degradation.

### C3. "Existing Caddy detection" is a footgun (MEDIUM-HIGH)

**What's wrong.** Spec §6.4.2 Probe A: before launching, Collabhost pings `http://localhost:2019/config/`. If something answers, Collabhost skips its own launch and points `ICaddyClient` at 2019 instead.

Consider three failure modes:

1. **Operator is running Caddy for a separate project.** Their existing Caddy has its own routes, its own TLS config, its own admin auth (Caddy admin API supports it). Collabhost now starts POSTing its own routes to their Caddy via `/load`. Caddy's `/load` replaces the *entire* config atomically. We have just obliterated the user's existing config.
2. **Operator is running another instance of Collabhost.** Now both instances race on `/load`. Whichever one seeds last wins. Routes appear and disappear depending on supervisor timing. This is a nightmare to diagnose.
3. **Something else happens to be bound to 2019 and returns 200 on `/config/`.** Unlikely but not impossible; port 2019 is Caddy's *convention*, not a reservation.

The spec says the probe is "1 second timeout, GET `/config/`, 2xx means present." None of these failures register as a false positive at the probe level -- they all return 2xx. The probe can't distinguish "friendly existing Caddy that wants Collabhost to manage its routes" from "adversarial existing Caddy that absolutely does not."

**Why it matters.** The coordinator-side intent of Probe A is "Collabhost is a friendly citizen that defers to operator-managed Caddy." But the implementation semantics are "Collabhost silently takes over whatever's on localhost:2019." These aren't the same thing. The community-standard pattern (the research names Tauri and FrankenPHP) is *bundled binary + env var override* -- nobody else does automatic detection of a foreign process on a conventional port.

**Recommended change.** Pick one of:

- **Option A (safest):** Drop Probe A entirely. The escape hatch is `COLLABHOST_CADDY_PATH`. Operators who want to run their own Caddy do so via env var + a documented note that they need to forward admin API access. This is what FrankenPHP and Tauri do. No magic.
- **Option B (middle ground):** Keep Probe A but require a *marker* in the existing Caddy's config before adopting it. Collabhost POSTs a known sentinel (e.g., sets a no-op apps.http.servers.collabhost-probe.listen entry) and fails if the existing config would conflict. This is the "safe detection" pattern but it adds code and still leaves race conditions.
- **Option C (explicit opt-in):** Add a new env var `COLLABHOST_CADDY_ADOPT_EXISTING=true`. Probe A fires *only* if this is set. This makes the behavior explicit, documentable, and impossible to hit by accident.

My strong preference is **A**. The env var covers 99% of the legitimate use case. The remaining 1% can be served by C if it ever materializes. Probe A as written trades real operator-footgun risk for a use case that nobody in the evidence base actually implements.

If Bill wants to keep Probe A, then the concurrent-startup race (failure mode 2) needs a file-lock or named-mutex mechanism at a minimum. The spec does not address concurrent startup at all.

### C4. Escape-hatch priority ordering is almost right, but one swap improves it (MEDIUM)

**What's wrong.** Spec §6.4.1 order:

1. `COLLABHOST_CADDY_PATH` env var
2. (Probe A -- external Caddy on 2019) -- handled at registration time, §6.4.2
3. Bundled sidecar
4. Legacy `Proxy:BinaryPath` config

I've already argued Probe A should go (C3). Setting that aside, the remaining ordering is `env > bundled > config`. That's backwards for dev ergonomics.

**Why it matters.** Developers run from an IDE. They will have `appsettings.Development.json` pointing `Proxy:BinaryPath` at their local `caddy` install. They will not have a bundled sidecar at `AppContext.BaseDirectory` because `dotnet run` from the source tree doesn't have one. Under the proposed order, once a bundled sidecar exists in a developer's `bin/Debug/net10.0/` (because they ran a `Publish` locally at some point, or because someone checked in a dev binary artifact by accident), it silently takes precedence over their config. That's a "why is this suddenly using a different Caddy?" diagnosis session.

**Recommended change.**

- Order should be `env > config > bundled` for resolution of an operator-specified path.
- Rationale: env is the always-explicit escape hatch. Config is the operator's explicit choice in `appsettings`. Bundled is the fallback for the no-configuration case.
- This also matches ASP.NET Core's own config priority convention (explicit > fallback).
- The `appsettings.json` default moves from `"caddy"` to empty string (as proposed). Dev environments still set `"Proxy:BinaryPath": "caddy"` in `appsettings.Development.json` and get the PATH-resolved dev binary.

### C5. Caddy version pin: plain-text file is right, `caddy.version` is wrong (LOW-MEDIUM)

**What's wrong.** Remy proposes `caddy.version` at the repo root. The content choice (plain text, one line) is correct. The filename is not -- it reads like a filename for "this codebase's version," not "the pinned version of our Caddy dependency."

**Why it matters.** Naming is architecture. A developer scanning the repo root sees `caddy.version` and their first assumption is "Caddy's own version file." That's a 5-second confusion for every new contributor. We can do better.

**Recommended change.** Rename to `.caddy-version` (dotfile, kept with the other cross-cutting pins) or `caddy.pin.txt` or move it under `release-assets/caddy.version`. The `release-assets/` co-location is my favorite -- it groups with `caddy-LICENSE` and `caddy-NOTICE` so all Caddy-related pinned content is in one place, and the filename `caddy.version` is unambiguous because it's in a Caddy-named folder.

Not a blocker. Flag it now, fix it in the same PR as Phase 4.

### C6. CI runner choice: native per-platform is correct, but spec undersells the cost (LOW)

**What's wrong.** Spec recommends native runners (`windows-latest`, `macos-latest`, `ubuntu-latest`) over Collaboard's all-`ubuntu-latest`. The forward-compat argument (signing, notarization, platform-native archive tools) is valid. But the cost framing -- "~2-3 min overhead per leg" -- understates the real cost, which is the *matrix maintenance overhead*. `macos-latest` changes ARM version annually. Windows runners periodically break PowerShell behaviors. Scripts that work on Ubuntu `tar` sometimes break on macOS `tar` (BSD vs GNU flags). Collaboard's single-runner approach pays for itself in maintenance-simplicity terms beyond CI wall clock.

**Why it matters.** The forward-compat argument is real but it's future-cost-vs-future-cost. The *current* cost of running everything on `ubuntu-latest` with cross-RID publish is one runner-flavor's worth of CI maintenance for the release track. The current cost of going native per-platform is three runner-flavors' maintenance.

**Recommended change.** Remy's recommendation is defensible. My preference is to stay on `ubuntu-latest` for all five RIDs in v1 and only break out native runners when signing or notarization actually arrives (decision 17 deferred both). But this is a judgment call, not a correctness issue, and I'd sign off either way.

If we go native, the spec should explicitly note:

- Who owns updating matrix image pins when GitHub deprecates a runner image (Nolan, presumably).
- The `tar`-flag pitfall: macOS `tar` rejects `-C <dir> .` in some versions; use `--directory <dir> .`.
- The `zip` shell step uses `Compress-Archive` (PowerShell) -- confirmed safe because windows-latest always has `pwsh`, but state it explicitly.

### C7. Post-launch probe: 5 seconds is reasonable, but there's a hidden failure mode (LOW-MEDIUM)

**What's wrong.** Probe B (post-launch) has deadline = Now + 5s, polling every 200ms, `IsReadyAsync` timeout 5s. There's a subtle race. Caddy's admin API may *accept the TCP connection* before it's ready to serve requests. Current `IsReadyAsync` tolerates this via `GET /config/` returning 2xx -- that's correct. But: when `HttpClient` races with Caddy's own socket listener coming up, the first GET may time out at the HttpClient level rather than return a clean 404/503/etc. The deadline-vs-timeout interaction then has a 5s inner timeout inside a 5s outer deadline -- effectively one attempt.

**Why it matters.** The loop as written:

```
deadline = Now + 5s
while Now < deadline:
    if await IsReadyAsync(5s timeout): return true
    delay 200ms
```

If the first call to `IsReadyAsync` hangs for the full 5s, we exit the loop with zero retries. That's probably fine in the happy case (Caddy was ready in <1s or not at all). But for borderline-slow starts (ARM runner cold boot, SSD under load), we may soft-fail prematurely.

**Recommended change.** Either:

- **Tighten the per-attempt timeout.** `IsReadyAsync(ct)` should take a caller-supplied cancellation, and the loop should pass a per-attempt token with a 1s deadline rather than relying on `HttpClient.Timeout`. This matches the existing `IsReadyAsync` signature.
- **Extend the outer deadline to 10-15s.** 5s is aggressive for a cold-start Caddy on first launch when disk caches are cold. Aspire uses 30s for its typical resource health gate. Collabhost can afford 10s at startup before calling the probe dead.

My preference is both -- per-attempt 1s timeout, outer deadline 10s. Gives us 10 attempts under the budget decision 10 names.

### C8. Env var floor design is correct, but naming needs a small polish (LOW)

**What's wrong.** Spec proposes five `COLLABHOST_*_PATH` env vars. Good. But `COLLABHOST_TEMP_PATH` is misleading -- it's not a "temp files" directory, it's specifically the scratch directory for the Caddy bootstrap config. Naming it TEMP implies operators should route /tmp through it.

**Why it matters.** Operators who see `COLLABHOST_TEMP_PATH` will set it to `/var/cache/collabhost/` or similar. Then they'll wonder why their actual temp files don't go there. The name overclaims scope.

**Recommended change.** Rename to `COLLABHOST_PROXY_CONFIG_PATH` or `COLLABHOST_RUNTIME_PATH`. My preference is the latter -- `COLLABHOST_RUNTIME_PATH` signals "transient runtime artifacts" which is accurate and gives us room to put other runtime scratch data there (log buffers overflow? pid files? later).

### C9. Archive self-check for version match is smart, but misses cross-platform coverage (LOW)

**What's wrong.** §15.3 proposes a CI self-check step: extract archive, run `--version`, assert version matches. Implementation uses `if [ "${{ matrix.rid }}" = "linux-x64" ]` -- only linux-x64 self-tests.

**Why it matters.** The check is most useful when we break a platform leg specifically. linux-x64 is the easiest leg to get right; win-x64 and osx-arm64 are more likely to ship broken.

**Recommended change.** Extend to all legs where the RID can actually run on the matrix OS:

- `linux-x64` runs on `ubuntu-latest`: yes
- `win-x64` runs on `windows-latest`: yes
- `osx-arm64` runs on `macos-latest` (Apple Silicon runners): yes if macos-14 or newer
- `osx-x64` runs on `macos-latest` (Rosetta, likely slow): skip
- `linux-arm64` cannot run on ubuntu-latest (x64): skip

Three of five legs get coverage. 50% → 60% is a modest lift; it's worth doing.

---

## 4. The Six Open Questions -- Independent Answers

### Q1: Hard-fail vs soft-fail on Caddy probe timeout

**My answer: soft-fail, but ONLY paired with the visibility fixes in concern C2.** Remy's right that the API is still useful without proxy, but silent degradation is worse than hard-fail. If we soft-fail, the system-status response and the dashboard must loudly indicate "proxy subsystem disabled; reason: probe timeout; remediation: here." Without that, hard-fail wins because at least the operator knows something is broken.

**Where I disagree with Remy's framing:** Decision 10 says "hard-fail with clear log message." Remy reframes this as soft-fail because hard-failing the whole API is too aggressive. He's right on the aggression point but the original decision still wants a *clear fail*, not a degraded-silent-success. The spec needs both halves -- soft-fail at the process level, loud-fail at the observability level. That's what I'd like Bill to confirm, not just "soft-fail yes/no."

### Q2: `--version` stdout format (bare `0.1.0` vs `Collabhost 0.1.0`)

**My answer: Match Collaboard. Print `Collabhost 0.1.0`.** Remy prefers bare for "machine consumption." I disagree:

- Two tools in the Collab suite (Collaboard, Collabhost) with different --version formats is an inconsistency we'll regret.
- "Machine consumption" prefers `--version --format=json` or `--version --short`. That's the right way to do it if we eventually need it. Do not pre-optimize for machines at the expense of readability.
- When an operator runs `./some-binary-in-my-PATH --version` and gets `0.1.0` back, it's ambiguous. `Collabhost 0.1.0` self-identifies. This matters more than Remy suggests.

### Q3: Auto-clear `com.apple.quarantine` in `install.sh`

**My answer: Yes, auto-clear.** Remy's tradeoffs list is right and the threat-model argument is right -- if someone can trick a user into piping install.sh, they can insert arbitrary commands. The quarantine clear is a small convenience inside a larger trust decision the user already made. Plus, `xattr -d` with `|| true` is idempotent and safe on re-run.

Add to the INSTALL.md note that if someone downloads the archive manually and runs the binary without install.sh, they need to do this themselves. Spec already has that copy.

### Q4: Dry-run workflow in #153 or follow-up

**My answer: Follow-up, as Remy recommends.** Shipping #153 at the scale the spec already describes is substantial. Dry-run is a "make the publishing process better" task; the first release doesn't need it because the first release *is* the validation (we cut v0.1.0, we see what breaks, we fix forward). Don't bloat #153 with infrastructure we'd need for releases 5 through 50 -- we need it for release 1 only after release 1 happens.

### Q5: Archive filename includes version

**My answer: Omit the version, as spec currently proposes.** Two reasons:

1. Install script URL construction stays dead simple: `collabhost-{rid}.{ext}` is the same filename across releases.
2. Operators who pass archives out-of-band can rename them locally; the tag in the GitHub Release page captures version context.

I do not hold this strongly. If Bill prefers version-in-filename for operator clarity, it's a 5-line workflow change. But I'd default to Collaboard's behavior because matching the sibling project's conventions is worth small ergonomic trades.

### Q6: Diagnostics in `/api/v1/status` vs `/api/v1/version`

**My answer: Keep them separate, as Remy proposes.** `/api/v1/version` stays single-purpose ("what version is running") -- that's a common enough question to deserve its own endpoint with the most predictable shape. `/api/v1/status` is the place for everything else (hostname, uptime, proxy state (C2), eventually health summaries). If we ever need build date or commit hash, they go under a nested `diagnostics` object in `/api/v1/status`, not flattened into `/api/v1/version`.

This is also why I'd like /api/v1/status extended rather than carving a new /api/v1/diagnostics. One status endpoint, increasingly rich over time, is easier to evolve than N purpose-specific endpoints.

---

## 5. Phase Sequencing Analysis

Remy's phases:

1. Version helper + CLI + endpoint
2. Escape-hatch binary resolution + probe
3. Data path + env var floor
4. Release workflow + Caddy bundle + checksums
5. Install scripts + GitHub Pages

**Overall verdict: the ordering is sound. Two adjustments.**

### Ordering works because:

- Phase 1 is net-positive regardless of the release effort. Ships alone if everything else slips.
- Phase 2 (Caddy resolution) is where the most architectural risk lives; isolating it makes the PR reviewable.
- Phase 3 is additive; env vars with no readers are inert, then readers get added.
- Phase 4 depends on Phases 1-3 being in main (version threading, env var reads, proxy resolution all get exercised).
- Phase 5 is meaningless without Phase 4 producing artifacts.

### Adjustment 1: Phase 2 must include the migration/typestore/seed move, not just the proxy seed move.

Call it Phase 2a if you want to keep Phase 2 focused on the Caddy resolver. Alternative: carve out a new Phase 1.5 "Production startup posture" that does:

- Move EF migrations out of `IsDevelopment()` gate, add pre-migration backup.
- Move `TypeStore.LoadAsync` + `StartWatching` out of `IsDevelopment()` gate.
- Move `ProxyAppSeeder.SeedAsync` out of `IsDevelopment()` gate (current Remy scope).

These three are coupled -- they're all part of "what does first-run production look like" -- and splitting them forces awkward merge choreography. Keep them together.

### Adjustment 2: Phase 3 should ship before or alongside Phase 2, not after.

The `COLLABHOST_CADDY_PATH` env var reader lives inside Phase 2's `CaddyResolver`. The other env var readers (DATA, USER_TYPES, TOOLS) don't depend on Phase 2. If we ship Phase 2 before Phase 3, Phase 2 acquires one of the five env vars and ships a partial floor. That's awkward.

**Better:** Phase 3 first (all five env var readers go in, but without the Caddy resolver priority chain), then Phase 2 (adds Probe logic on top of an already-honored `COLLABHOST_CADDY_PATH`). This gives us an incrementally useful first merge ("operators can override paths via env") without dragging the Caddy refactor along.

Put differently: Phase 3 is Phase 2's prerequisite if we decouple the env-var-reading code from the resolver priority-chain code. Worth doing.

### No hidden dependencies I haven't named.

Phases 1 and 3 are fully independent. Phase 2 reads the version helper (from Phase 1) for logging. Phase 4 compiles against everything. Phase 5 runs against Phase 4's artifacts.

---

## 6. Code Seams Audit

Every file Remy cited, I read. Notes below.

### `backend/Collabhost.Api/Proxy/CaddyClient.cs`

Remy's understanding: correct. `IsReadyAsync` exists, uses relative URI `config/`, returns `false` on exception. Matches his Probe B design. No issues.

One small note: the existing `CaddyClient` catches `Exception`, which is broader than necessary. If we lean on it for the probe, we may want to narrow to `HttpRequestException` / `TaskCanceledException`. Not a blocker -- that's a separate cleanup opportunity.

### `backend/Collabhost.Api/Proxy/_Registration.cs`

Remy's understanding: correct on the mechanics, understated on the impact of Probe A. Line 21 confirms `PortAllocator.AllocatePort()` is used -- **card #83 is indeed fixed.** Line 34 sets the HttpClient base address to `http://localhost:{AdminPort}`. For Probe A to redirect the HttpClient to `localhost:2019`, we'd need to restructure this registration. Remy's spec lists that as a change but the change is more invasive than "register the HttpClient differently" -- it means the `AdminPort` field on `ProxySettings` becomes conditionally used, and `ProxyArgumentProvider` must also skip its bootstrap-config write (because we're not launching our own Caddy in that branch). Remy lists this as one line in the code seams table; I'd call it out as a structural refactor.

### `backend/Collabhost.Api/Proxy/ProxyAppSeeder.cs`

Remy's understanding: correct. `ResolveBinaryPath` is the PATH-resolution function (lines 156-209). Extraction to `CaddyResolver.cs` with a priority chain is a clean refactor. The current `ResolveFromPath` shell-out to `where`/`which` stays as Priority 4 (legacy config path) but its purpose shrinks.

One gap: the seeder's existing log message (lines 55-62) hardcodes the "install Caddy via winget" guidance. After the refactor, this message should change to point at the bundled location and mention `COLLABHOST_CADDY_PATH`. Remy's spec doesn't call this out; easy to fix during implementation but worth naming.

### `backend/Collabhost.Api/Proxy/ProxyArgumentProvider.cs`

Remy's understanding: correct -- argument provider is unaffected by the resolver change *when we're launching our own Caddy*. However, when Probe A fires and we don't launch our own, we must *not* register the `ProxyArgumentProvider` as `IProcessArgumentProvider` because there's no process to augment. Remy's spec §6.5 names this in passing; the implementation needs a clean "skip this registration" branch, not a no-op attempt-to-write-bootstrap-config.

### `backend/Collabhost.Api/Proxy/ProxySettings.cs`

Remy's understanding: correct. `BinaryPath` is `required string` (line 9). Relaxing to nullable/default-empty is a real source change. `AdminPort` is set-able (line 18) and allocated dynamically -- the model supports the "external Caddy = AdminPort unused" state.

### `backend/Collabhost.Api/System/SystemEndpoints.cs`

Remy's understanding: correct. Line 21-23 reads `AssemblyInformationalVersionAttribute` without stripping `+hash`. This is exactly the drift Remy's version helper fixes. Replacing with `VersionInfo.Current` is a 3-line diff.

Note: namespace is `Collabhost.Api.Platform` already -- Remy's placement of `VersionInfo.cs` under `Platform/` is consistent.

### `backend/Collabhost.Api/Mcp/_McpRegistration.cs`

Remy's understanding: correct. Line 14 reads the attribute. Would move to `VersionInfo.Current`.

### `backend/Collabhost.Api/Mcp/DiscoveryTools.cs`

Remy's understanding: correct. Line 60-62 is the third duplicate read. Move to `VersionInfo.Current`.

### `backend/Collabhost.Api/Program.cs`

Remy's understanding: partially correct, see C1. He names the `ProxyAppSeeder.SeedAsync` call inside `IsDevelopment()`. He does NOT name the EF Core migration call (line 76) or the TypeStore load (lines 79-81) which are in the same block. This is the biggest hole in the spec.

### `backend/Collabhost.Api/Data/_Registration.cs`

Remy's understanding: correct on structure. Line 14 has the default connection string `./db/collabhost.db`. The spec proposes moving the default to `./data/collabhost.db` to match locked decision 13. This is a one-line change.

However, the spec does not propose adding a migration-backup mechanism here (see C1). If we're promoting this file to carry production migration responsibility, the backup belongs next to the connection string wiring.

### `backend/Collabhost.Api/appsettings.json`

Remy's understanding: correct. The `Proxy:BinaryPath` default is `"caddy"` (line 16). Spec proposes removing or setting to empty string; either way, `ProxySettings.BinaryPath` cannot remain `required`. This is linked to the change in `ProxySettings.cs` -- they must move together.

---

## 7. Risks Remy Did Not Call Out

### R1. Probe A concurrent-startup race (see C3).

Two Collabhost instances starting simultaneously (e.g., operator runs two data directories side-by-side for an upgrade test) both hit localhost:2019, both skip their own launch, both push routes to the same Caddy, both crash differently. Not in the spec's risk table. Should be.

### R2. Removing the `IsDevelopment()` gate for migrations introduces migration-on-startup for every production upgrade.

Collaboard backs up first. Collabhost doesn't. A bad migration on a production install with real data loses that data. The spec acknowledges this in section 17 as "Migration-on-startup is not a new pattern in .NET apps; it's just new to Collabhost production" but does not commit to the backup discipline that makes it safe. This risk is medium-impact, medium-likelihood, and the mitigation is small. Should be a first-class risk with a concrete action item.

### R3. GitHub Pages install-script TTL when we flip to `docs/CNAME`.

When Bill eventually buys `collabhost.dev`, the install URLs change from `mrbildo.github.io/collabhost/install.sh` to `collabhost.dev/install.sh`. Older README snapshots (cached by Google, archived by users) will keep pointing at the old URL indefinitely. GitHub Pages auto-switching when CNAME is added means the old URL may break. Worth a line in §10.6: "When CNAME activates, keep the `mrbildo.github.io` URLs redirecting for at least 6 months" (GitHub Pages supports custom 301 redirects via the same `docs/` folder if needed).

### R4. Self-contained single-file + antivirus.

Windows Defender (and enterprise AV like CrowdStrike) flag self-extracting single-file .NET binaries on first run. Collaboard has this problem too but hasn't hit scale where enterprise users report it. The first Collabhost user to land in a managed-IT environment will see SmartScreen or AV quarantine. Not a blocker for v1 but deserves a line in INSTALL.md alongside the macOS Gatekeeper section: "Windows: SmartScreen may prompt on first run; click 'More info' → 'Run anyway'."

### R5. Caddy binary download has no checksum verification.

Listed in §17.1 but the mitigation "trivial to add" is understated -- it's a real gap. Caddy's release page provides `caddy_{ver}_{platform}.txt` checksum files alongside the archives. Five lines of CI to verify. Ship this in Phase 4, not as a "TODO later."

### R6. The install script fetches `releases/latest` which may include a failed build.

If a release is published but the workflow fails mid-matrix, GitHub shows the release with partial artifacts. `releases/latest` returns it. Install scripts try to pull a missing or corrupted archive. Mitigation: the workflow already uses `--clobber`, so re-running succeeds; but the install script should exit cleanly when `checksums.txt` is missing or an expected archive is absent, with a clear message. Spec addresses the checksum-missing case (§9.5) but not the archive-missing case.

### R7. Five archives at ~55 MB each = 275 MB of GitHub Release storage per release.

GitHub Releases have no documented hard limit, but they do have soft limits for individual asset count and total repo storage. Over many releases this compounds. For 50 releases: ~14 GB. Worth noting as a boundary we'll hit eventually, not a v1 blocker.

### R8. No consumer of `--version` on day 1.

Remy adds `--version`. Who calls it? INSTALL.md's troubleshooting section. That's it. If operators don't call it, the flag atrophies. Worth committing to: the `install.sh` post-install smoke test should run `$INSTALL_PATH/Collabhost.Api --version` and echo the result, so every installation validates version threading. This is cheap belt-and-suspenders.

---

## 8. Questions for Bill

1. **Probe A: keep, drop, or gate behind explicit opt-in?** Remy and I disagree here. My recommendation is drop. If you want to keep it, are you comfortable with the concurrent-startup footgun (C3, R1)?

2. **Migration on startup with backup -- yes or no?** This is the production-upgrade model. Collaboard does it. Remy's spec gestures at it but doesn't commit. I'd like an explicit call before Phase 2a ships.

3. **Native CI runners or all-ubuntu-latest?** My preference is all-ubuntu-latest for v1 because signing/notarization is deferred. Remy's preference is native for forward compat. Both are defensible; your call.

4. **`--version` format: bare or prefixed?** Remy prefers bare. I prefer `Collabhost 0.1.0` to match Collaboard. Small but worth settling before implementation.

5. **Phase 2 vs Phase 3 order:** Remy's spec has 2 before 3. I argue 3 should land first because it ships incremental value and cleanly separates env-var-reading from resolver-priority-chain concerns. Your call on whether that resequencing is worth the rewrite.

6. **Should CLAUDE.md's "Known Issues" entry for #83 go away in the same PR as the spec-approval merge, or as a standalone doc PR?** Tiny thing. Either is fine but worth deciding so Nolan knows which PR owns it.

---

## 9. Observations

### O1. Card #83 confirmed fixed.

Verified independently. `backend/Collabhost.Api/Proxy/_Registration.cs:21` uses `PortAllocator.AllocatePort()`. Port 2019 appears in the codebase only in test fixtures (`ProxyConfigurationBuilderTests`, `ProxyManagerTests`, `RouteExplicitStateTests`) and in the new Probe A design (where it's the documented external-Caddy convention). Remy's anomaly note in §17.3 is correct. Recommend closing card #83 and updating `CLAUDE.md` "Known Issues" in a trivial doc-only PR.

### O2. `ProcessSupervisor` logs suggest a state-management coupling worth noting.

`ProxyManager.StartAsync` (line 97-108) silently returns when the proxy app is not registered. The log message reads "ensure the proxy binary is available and restart Collabhost." That's stale guidance once the escape hatch lands -- the message should point at `COLLABHOST_CADDY_PATH` and the bundled fallback. Not a blocker, just a copy update that gets missed if no one looks.

### O3. `IsReadyAsync` catches `Exception`.

Line 32 in `CaddyClient.cs` uses blanket catch. Broad exception catches are a lint/analyzer smell and this is load-bearing in the new probe. Consider narrowing to `HttpRequestException` / `TaskCanceledException` / `OperationCanceledException` during Phase 2. Pre-existing but exacerbated by increased reliance.

### O4. Spec has "Collaboard's model" consistency with one gap.

The spec repeatedly cites Collaboard patterns (artifact naming, `--clobber`, release event trigger, workflow shape). Good discipline. But the migration-on-startup-with-backup pattern is Collaboard's and the spec silently omits it. If we're inheriting conventions, inherit all of them -- or explicitly reject them with reasoning. C1 and R2 both follow from this gap.

### O5. The five-path env var list is comprehensive but does not cover one existing path.

Spec's env var table lists: DATA, USER_TYPES, TOOLS, CADDY, TEMP. What about the Caddy log file path? `ProxyArgumentProvider` writes bootstrap config but does not currently control where Caddy itself logs. In a production install, operators may want Caddy's logs co-located with Collabhost's logs. If the data path is overridable, proxy logs should be too -- but this crosses into configuration territory that is probably #104's remit, not #153's. Flag and defer.

### O6. "File watcher on TypeStore in production" is an unacknowledged behavior change.

Current `IsDevelopment()` block calls `typeStore.StartWatching()`. If we move it out of the dev gate (per C1), production installs now have an active filesystem watcher on `UserTypes/`. That's probably desired behavior (live-reloading type definitions on disk change) but it's a footprint change: one more OS file-handle, one more Linux inotify watcher. Worth acknowledging in the spec, not because it's wrong but because it's a behavior promotion from dev to production that deserves the name.

### O7. Frontend has no UI for "proxy externally managed."

Searched `frontend/` for "external" / "externally managed" / "proxy.*external" -- no matches. If Probe A survives the review, the UI needs to display a "proxy: external" state on the dashboard and on the proxy app detail page. That's Dana's scope, but the spec should name it as a cross-team coordination item. Current spec treats the external-proxy case as a backend detail; it's actually a UI affordance that needs to exist.

### O8. macOS Gatekeeper note in INSTALL.md is good, missing equivalent for Windows SmartScreen.

See R4. A one-paragraph "Windows: SmartScreen may prompt" section in INSTALL.md §11 costs nothing and catches the first enterprise operator who hits it.

---

## Appendix: Verification Summary

| Item | Status |
|------|--------|
| Read spec in full (`release-pipeline.md`, 1438 lines) | ✓ |
| Read locked decisions (card #153 description) | ✓ |
| Read `collaboard-release-recon.md` | ✓ |
| Read `bundled-deps-research.md` | ✓ |
| Read `install-patterns-research.md` | ✓ |
| Verified all 11 named code seams | ✓ |
| Verified card #83 is fixed (PortAllocator at `_Registration.cs:21`) | ✓ |
| Searched for existing external-proxy UI | ✓ (none exists) |
| Searched for migration backup pattern | ✓ (none exists) |

Review branch: `review/153-marcus`, spawned from origin/main. No code changes. Single deliverable: this file.

---

*Review complete.*
