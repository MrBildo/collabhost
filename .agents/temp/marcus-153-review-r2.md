# Marcus Review R2 -- Release Pipeline Spec (#153)

**Reviewer:** Marcus (architecture advisor)
**Date:** 2026-04-18
**Spec under review:** `.agents/specs/release-pipeline.md` on `spec/153-release-pipeline-r2` @ `c0a085e` (1668 lines)
**Prior review:** `.agents/temp/marcus-153-review.md` on `spec/153-release-pipeline` (R1)
**Review branch:** `review/153-marcus-r2` (from `spec/153-release-pipeline-r2`)

---

## 1. Verdict

**Ship-ready with targeted changes.** Remy has taken the R1 catches seriously and come back with a spec that is materially better than R1 in vocabulary, phase ordering, and scope discipline. The #159 settings-resolution model is integrated cleanly. The #156 carve-out is the right call. Probe A is gone. The escape-hatch-vs-precedence-chain confusion is dissolved.

The remaining work is not architectural re-design. It is:

- Closing two operator-contract questions Remy already surfaced (§17.2 Q7 `appsettings.json` reinstall, Q3 log directory) with a Bill decision rather than another round of spec revision.
- Fixing three concrete implementation-level defects that the R2 design masks but doesn't introduce (CaddyClient has **three** blanket catches, not one; `ProxyManager.CurrentState` concurrency is undefined; `appsettings.Local.json` load order in `Program.cs` contradicts the locked §2.5 precedence in dev).
- Tightening one scope boundary (Phase 4 requires more concrete contract from #156 than the spec acknowledges).

None of these block merging the spec. They block Phase 2 and Phase 4 implementation, in that order.

**Phases 1 and 3 are green-light as written.** Phase 2 needs the three implementation-level defects folded into its scope. Phase 4 depends on #156's spec being written before it can be sized credibly.

On Remy's three explicit asks: Phase 2/3 swap is what I had in mind and the text captures it correctly. `proxyState` enumeration is mostly right but needs one more state (see R2-C3). `CaddyClient` catch narrowing does **not** belong in Phase 2 as a single-line change -- it's a three-site hygiene pass that should land in Phase 2 as a coherent block, not sprinkled in.

---

## 2. R1 Concerns -- Status

| R1 Concern | R2 Status | Notes |
|------------|-----------|-------|
| **C1. Migration gate removal is larger than spec acknowledges** | **Addressed via carve-out** | §6.5 + §14.6 + §18 "External #156" correctly name #156 as hard prerequisite. Scope boundary is clean. Phase 4 explicitly cannot ship without #156 merged. See R2-C1 below for what #156 still owes #153. |
| **C2. Soft-fail needs visibility story** | **Addressed** | `proxyState` field on `/api/v1/status` with four values (§6.4.2). Dashboard rendering named as Dana's contract. This is exactly the shape I argued for. Two small concerns carried to R2-C3. |
| **C3. Probe A is a footgun** | **Addressed (dropped)** | §6.4 drops Probe A entirely. `COLLABHOST_CADDY_ADOPT_EXISTING=true` parked as future explicit opt-in. Clean. |
| **C4. Escape-hatch priority order** | **Addressed** | §6.4.1 locks `env > config > bundled`. Matches ASP.NET Core convention and §2.5's resolution model. |
| **C7. Per-attempt probe timeout** | **Addressed** | §6.4.2 pseudocode shows `perAttemptTimeout = 1 second` inside the 5s outer deadline. Exactly the fix. |
| **Phase 2 / Phase 3 swap** | **Addressed** | §18 shows Phase 3 before Phase 2 with clear rationale ("Phase 3 is additive; Phase 2 adds the resolver on top"). Matches what I proposed. |

R1 minor concerns (C5 `caddy.version` filename, C6 native runners, C8 env-var naming, C9 cross-platform self-check): Remy has kept the original positions on all of these. I did not argue any of them hard in R1 ("Not a blocker. Flag it now, fix it in the same PR as Phase 4." for C5; defensible either way for C6; C8's `COLLABHOST_TEMP_PATH` is dropped entirely in R2 §12.3). I'm dropping them all. They were noise then; they're noise now.

---

## 3. Remy's Three Explicit Asks

### 3.1 Phase 2/3 swap -- is this what I had in mind?

**Yes.** §18 captures the swap correctly. The rationale Remy writes for Phase 3 ("additive; env vars with no readers are inert until readers land") is the argument I made in R1 §5. Phase 2 landing on top of an already-honored `COLLABHOST_CADDY_PATH` from Phase 3 is the clean seam.

One small note on the phase plan text: §18 Phase 3 scope says "Wire `COLLABHOST_CADDY_PATH` through `ProxyAppSeeder.ResolveBinaryPath` (existing code path) -- full `CaddyResolver` refactor lands in Phase 2 but the env-var read itself is additive." This means Phase 3 introduces a *partial* Caddy resolver (env var honored, bundled not yet honored, config-fallback preserved). That's fine as an interim state -- the operator who sets `COLLABHOST_CADDY_PATH` in Phase 3's world gets the intended behavior. But it does mean Phase 3 ships with a half-resolution that gets rewritten in Phase 2. Call that out in Phase 3's description explicitly: "after Phase 3 and before Phase 2, the resolver is env-var-aware but does not yet honor the bundled sidecar." Otherwise an implementer between phases will wonder what `Proxy:BinaryPath = ""` means for their local dev.

**Verdict: ship as written with that one-line clarification.**

### 3.2 `proxyState` enumeration + `CurrentState` exposure pattern -- valid?

**Mostly yes. Three concerns, ranked by impact.**

**(a) Concurrency hazard on `CurrentState` is undefined.** `ProxyManager` is registered as a singleton hosted service. `SystemEndpoints.GetStatus` runs on a request thread. When the probe completes on the hosted-service background worker and writes `CurrentState = "running"`, the request thread reading `CurrentState` has no memory barrier guaranteeing it sees the write. A plain `public string CurrentState { get; private set; }` auto-property is **not** thread-safe for cross-thread reads of reference-type state.

The spec doesn't specify a memory model. It should pick one. Options:

- **Option A (simplest):** `private int _currentState;` backed by an enum-shaped int, accessed via `Interlocked.Exchange` / `Volatile.Read`. Map to the string on read.
- **Option B (equivalent):** `private volatile ProxyState _state;` where `ProxyState` is a C# enum. Maps to string at the endpoint boundary. This is my preference -- keeps the state typed inside the manager, stringifies at the API boundary.
- **Option C:** Guard reads/writes with a lock. Overkill for a single-field state; skip.

The spec's "`ProxyManager` exposes a `CurrentState` property" is under-specified. Pick Option B, write it into §6.4.2 before Phase 2 starts.

**(b) The enumeration is missing `"starting"`.** §17.2 Q6 flags this exact concern but parks it. I'd close it in the spec rather than letting it float: `"starting"` should be the initial value. The ≤5s probe window is a real user-facing concern -- if an operator hits `/status` during Collabhost's first 2 seconds of life, getting `"disabled"` or `null` is wrong (Caddy may very well come up fine). Dashboard gets a "starting" affordance and can show a spinner. Post-probe, the state settles to `running` / `failed` / `disabled`. `stopped` is only reachable if the operator manually stops the proxy app.

Lock the five-state enum before Phase 2: `starting | running | failed | disabled | stopped`. `"external"` stays parked for the future opt-in per §6.4.

**(c) The string-vs-enum decision should be named explicitly.** The spec uses string literals (`"running"`, `"failed"`, etc.) in the §6.4.2 wire contract. That's fine for the API surface but the *internal* representation should be a C# enum for exhaustiveness checks. `ProxyManager.CurrentState` returns a `ProxyState` enum; the endpoint layer maps to lowercase string. This aligns with the "lowercase status strings" convention named in CLAUDE.md's API conventions and avoids stringly-typed state inside the manager.

**Verdict: the pattern is right; the three details (concurrency, fifth state, enum-vs-string) need to be locked before Phase 2 implementation, not during.**

### 3.3 `CaddyClient` blanket-catch narrowing -- Phase 2 or separate hygiene PR?

**Phase 2, but with corrected scope.** The spec (§6.5) names "line ~32" as the one site to narrow. That's `IsReadyAsync`. But `CaddyClient.cs` has **three** blanket `catch (Exception ex)` blocks:

- Line 32: `IsReadyAsync` (the probe path -- load-bearing for Phase 2)
- Line 64: `LoadConfigAsync` (the route-sync path -- called every time a managed app state changes)
- Line 93: `GetConfigAsync` (the config-read path -- used by `ProxyEndpoints`)

Narrowing only line 32 while leaving 64 and 93 on blanket catches creates a coherence problem: three methods in the same file, same class, same concern, two styles. That's the pattern that gets copy-pasted wrong by the next person who touches the file.

**Recommendation:** narrow all three as a block in Phase 2, scoped under the same commit as the probe. Not a separate PR. The narrowing is mechanical (same three exception types for all three methods: `HttpRequestException`, `TaskCanceledException`, `OperationCanceledException`), it touches one file, it's ~15 lines net. Spec should update §6.5's `CaddyClient.cs` row to say "narrow all three blanket catches in `CaddyClient` to HttpRequestException / TaskCanceledException / OperationCanceledException."

If Bill prefers to split it out as hygiene, I'd rather see it as a **blocking prerequisite PR** to Phase 2, not a follow-up. Landing Phase 2's probe logic on top of a narrowed `IsReadyAsync` is safer than the reverse.

**Verdict: do all three in Phase 2 as one block, not just the probe-path narrowing.**

---

## 4. New R2 Concerns (ranked by severity)

### R2-C1. Phase 4 can't be sized without #156's spec existing (HIGH)

**What's underspecified.** §14.6 and §18 correctly declare #156 a hard prerequisite and carve its scope out. But #156 is currently a **card**, not a spec. The card description (read on Collaboard) lists 8 topics: migration posture, pre-migration backup, migration failure handling, seeding contract, TypeStore in production, first-run detection, startup failure modes, upgrade story. That's a substantive design card -- Bill sized it L.

Phase 4 of #153 ships a GitHub Actions workflow that produces an archive. That part is independent of #156. **But Phase 4 also defines the installer behavior, and the installer behavior depends on what a production first-run looks like.** §9.7 "merge-safe update behavior" is written as if `data/` is the only operator state to preserve; that's correct today but #156 may introduce other things (a lockfile? a marker for first-run-complete? a backup retention directory?). §13 INSTALL.md outline has two sections explicitly tagged "pending #156" (admin key, log directory). The spec acknowledges this but does not say what Phase 4's go/no-go criterion is.

**Why it matters.** If Phase 4 ships the workflow and installer with assumptions about first-run behavior that #156 later contradicts, we either (a) ship a v0.1.0 with a working workflow but a broken first-run experience, or (b) rework the installer after #156 lands. Both are recoverable; neither is ideal.

**Recommended change.** Before merging this spec, agree on one of:

- **Option A (simplest):** Phase 4 is gated on `.agents/specs/production-startup.md` existing with scope locked, not just #156 being merged. This adds an explicit dependency edge that's already implicit.
- **Option B (more aggressive):** Phase 4 itself is split into Phase 4a (workflow only) and Phase 4b (installer + INSTALL.md). 4a depends only on Phases 1-3 of #153. 4b depends on #156 landing. This lets the workflow ship earlier and gives more time for #156 to settle before the installer freezes.

I lean toward **Option B** because it reduces the blast radius of #156 slipping. Bill's call.

**Either way:** add a subsection to §14.6 that enumerates the specific contracts Phase 4 consumes from #156: (1) where admin key lands on first-run stdout, (2) whether `data/` ever contains state the installer must preserve beyond the SQLite DB, (3) whether the binary emits any "first-run succeeded" marker the installer can verify. Right now these are implicit.

### R2-C2. Program.cs `appsettings.Local.json` load order contradicts §2.5 precedence in dev (MEDIUM)

**What's wrong.** `Program.cs:18` reads:

```csharp
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
```

This runs **after** `WebApplication.CreateBuilder(args)`, which has already set up the default config providers in order: `appsettings.json` → `appsettings.{Environment}.json` → user secrets (dev) → environment variables → command-line args. ASP.NET Core's config system is "last-added wins" -- so `appsettings.Local.json`, added on line 18, takes precedence over env vars *and* command-line args for any key it defines.

**Why it matters.** §2.5's locked precedence is `CLI > env > appsettings.json > default`. In production this is fine because `.Local.json` doesn't exist (per §2.5). **In dev, this is broken.** A developer who sets `COLLABHOST_DATA_PATH` in their environment expecting it to override their `.Local.json` gets the opposite.

For the three explicit env vars Remy introduces (`COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_CADDY_PATH`) this is moot because the spec's §12.4 reads them **directly** (`Environment.GetEnvironmentVariable`) rather than via the config system. Direct reads bypass the ASP.NET Core pipeline and honor the documented precedence manually.

But for any setting read through `IConfiguration` (all the other 7 production settings in §12.1), the precedence chain is broken in dev. An operator-turned-developer following the §2.5 locked model "env vars override appsettings.json" will hit surprising behavior locally.

**Why this matters for #153 specifically.** It doesn't matter for the three explicit env vars. It matters for the *expectation* §2.5 sets up. The locked model says the precedence is uniform (`CLI > env > appsettings > default`). The actual code enforces that only for the three settings Remy special-cases. For everything else, `.Local.json` wins over env vars in dev.

**Recommended change.** One of:

- **Option A:** Move the `AddJsonFile("appsettings.Local.json", ...)` call *before* `AddEnvironmentVariables` in the builder pipeline. This requires using `WebApplicationOptions` or manually reordering the providers via `ConfigurationBuilder.Sources`. Messy.
- **Option B:** After `CreateBuilder`, explicitly re-add env vars so they end up at the top of the source stack: `builder.Configuration.AddEnvironmentVariables();` on line 19. This is a one-liner and gives env vars final precedence in dev.
- **Option C:** Accept the divergence and document it: "in dev, `appsettings.Local.json` overrides env vars; in production, there is no `.Local.json` so env vars win. The §2.5 precedence describes production."

I lean **Option B**. It's the smallest change and preserves the mental model. Fold it into Phase 3 (env-var overrides) since that's where the precedence contract first gets honored.

**If Option C:** update §2.5 to document the dev exception explicitly, so nobody expects dev to match prod here.

### R2-C3. `proxyState` wire shape -- startup window is undefined (MEDIUM)

Already covered in §3.2(b) above, but flagging it here so it shows up in the ranked list. §17.2 Q6 is the tracking question; Remy proposes `"starting"`. I concur, with the structural detail that this needs to be the **initial** value on `ProxyManager.CurrentState` (set in the field initializer or constructor), not a derived state. Otherwise there's a race where the status endpoint is hit before `ProxyManager.StartAsync` runs at all, and the state is effectively `null` or `default` -- neither of which is in the documented enum.

Lock `starting | running | failed | disabled | stopped` as the five-state set. Initial value = `starting`. Close Q6 in the spec.

### R2-C4. `ProxyManager.CurrentState` cross-thread read/write is undefined (MEDIUM)

Already covered in §3.2(a) above. Flagging here for the ranked list. Memory model must be named before Phase 2 implementation. Recommended: `volatile` enum field.

### R2-C5. Caddy download checksum verification is listed but not scoped (LOW-MEDIUM)

§17.2 Q2 says "recommend adding to Phase 4 directly." §18 Phase 4 scope says "Caddy download SHA256 verification step (§17.2 Q2 -- recommend including)." The recommendation is consistent. But Q2 is listed as open, which reads as unresolved to anyone skimming.

This was R5 in my R1 review. I still think it should ship in Phase 4 -- the threat model (Caddy's GitHub Releases gets compromised or proxied) is real enough, the mitigation is trivial (5 lines of bash per leg), and the alternative (skip it and hope) is out of character for a spec that otherwise invests in supply-chain integrity (SHA256 checksums on our own archives).

**Recommended change:** promote Q2 from "open" to "resolved: yes, included in Phase 4." One line in §17.3.

### R2-C6. `ProxySettings.BinaryPath` relaxation will cascade further than the spec names (LOW)

§6.5 says "`ProxySettings.cs` -- relax `BinaryPath` from `required`." Fine. But `ProxySettings` has FIVE `required` fields (`BaseDomain`, `BinaryPath`, `ListenAddress`, `CertLifetime`, `SelfPort`). Only `BinaryPath` loses `required`. The other four stay.

That's defensible -- the others have defaults in `appsettings.json` and are not expected to be unset -- but it means the `Get<ProxySettings>()` call in `_Registration.cs` line 15 behaves differently depending on which key is missing: `BaseDomain` missing → still throws; `BinaryPath` missing → returns an instance with `BinaryPath = null`. The registration code then has to decide what `null` means at that boundary (it doesn't today; the code assumes `Get<ProxySettings>()` either returns a fully-populated instance or null).

This is a C#-level detail, not an architectural one, but the spec's one-liner doesn't warn about it. The cleanest fix is to make `BinaryPath` default to empty string in the POCO (`public string BinaryPath { get; init; } = "";`) rather than nullable. Then `_Registration.cs` continues to work unchanged. Spec should say this.

**Recommended change:** §6.5 `ProxySettings.cs` row: "Change `BinaryPath` from `required string` to `string BinaryPath { get; init; } = "";` to keep the null-handling story clean."

### R2-C7. `VerifyCaddyReady` runs where? (LOW)

§6.4.2's pseudocode shows `VerifyCaddyReady(caddyClient, logger)` as a function. §6.5's `ProxyManager.cs` row says "On startup, after Supervisor promotes Caddy to `Running`, await `VerifyCaddyReady`." So the call site is `ProxyManager.OnProcessStateChanged` in the `Running` branch -- which currently calls `RequestSync()` after a 2-second sleep inside `ProcessSyncRequestsAsync`.

The existing 2-second delay (`ProxyManager.cs:202`) is there *specifically* because the Caddy admin API isn't ready immediately after the process goes to Running. With `VerifyCaddyReady`, that 2-second sleep becomes redundant -- the probe loop handles the ready-check explicitly. Spec says in §6.5: "Existing 2-second startup-delay code stays for now; can be revisited."

Why keep the 2-second sleep if the probe replaces its purpose? It's defensive code that becomes dead. Two concerns:

- Dead defensive code accumulates. If a future maintainer sees both the 2s sleep *and* the probe, they'll waste time figuring out which one is load-bearing.
- Total probe budget becomes 2s + 5s = 7s in the worst case, not 5s. That's fine for user-facing SLA but worth naming.

**Recommended change:** Either remove the 2-second sleep in Phase 2 (the probe replaces its purpose), or explicitly document why it stays. Don't leave it in a "can be revisited" state for unclear reasons. My preference: remove.

---

## 5. Position: Q7 `appsettings.json` Reinstall Behavior

§9.7 "Open design tension" lays out Option A (always overwrite) vs Option B (preserve if exists). Remy leans A with a loud warning; I agree, but I want to add a third option the spec doesn't name:

- **Option C: Overwrite the shipped file; write operator-edit guidance to INSTALL.md §5 "Configuration" that says "to persist customizations across reinstalls, use env vars; the shipped `appsettings.json` is overwritten on every reinstall."** This is Option A + explicit operator education, not A + warning-at-reinstall-time.

**My recommendation: Option C.**

Rationale:

1. **Option B's failure mode is worse than Option A's.** If an operator edits `appsettings.json` in v0.1.0 and the installer preserves it, then v0.2.0 ships with a new config key that has no default in the old file, the operator's install silently runs without that feature. This is the "config drift accumulates across versions" problem that every long-lived deployment hits. Option A at least keeps the shipped file in sync with the shipped binary.

2. **The warning-at-reinstall-time is ineffective.** The operator who read the warning on v0.1.0's install is not the one running the installer on v0.2.0 six months later. Warnings on transient stdout are one-shot; the canonical reference is INSTALL.md, which the operator re-reads when they want to customize.

3. **Env vars are the right persistent override mechanism** -- that's what §12.3 is for, and that's what the §2.5 resolution model says. The operator who wants to change `Proxy:ListenAddress` and keep it across reinstalls should set `COLLABHOST_PROXY_LISTEN_ADDRESS` once. (Which, per §12.3's deferred decisions, doesn't exist yet -- see §17.2 Q4. If Bill says "no env vars for proxy settings," then Option C forces operators to edit `appsettings.json` and re-edit after every update, which is tedious but not catastrophic for settings that change rarely.)

4. **Aspire's model is Option A.** The spec cites Aspire as the model; staying consistent with Aspire is a plus.

**What this means for §12.3's Q4:** if Bill picks Option C, then the decision to ship env vars for `Proxy:BaseDomain`, `Proxy:ListenAddress`, `Proxy:CertLifetime`, and `Proxy:SelfPort` becomes more important. Without them, operators who customize the proxy and want those customizations to survive reinstalls have no persistent path. My lean: ship all four `COLLABHOST_PROXY_*` env vars in Phase 3. Cost is ~4 × 5 lines of C# per-subsystem registration. Value is "the env-var floor is actually a floor."

**Verdict on Q7: Option C (overwrite + INSTALL.md education + ship more env-var overrides).** Document explicitly in §9.7 and in INSTALL.md §5. Close Q7 in §17.2.

---

## 6. Position: Q3 Log Directory

**§17.2 Q3** proposes writing Collabhost's stdout/stderr to `{DataPath}/logs/collabhost-{timestamp}.log` with rotation. Remy asks: "In scope of #153? Or push to a follow-up card?"

**My position: out of #153 scope. File as a follow-up card.**

Rationale:

1. **Log-directory design is its own concern.** Rotation policy, size limits, retention, filename convention, structured vs line-based output, integration with OpenTelemetry (Collabhost already uses OTel for its own telemetry via Aspire). Remy's "~30-line addition" is optimistic for anything that ships without regrets.

2. **The framing comes from Dana's "crashes before I see anything" friction-point.** That's a real operator concern, but it's addressable in multiple ways: (a) file-based log capture as Dana proposed, (b) Windows event log integration, (c) systemd journal integration on Linux, (d) forcing stdout to be captured by the install-script wrapper (e.g., `install.sh` ships a systemd unit file for Linux operators). Option (d) is genuinely cheap and covers the Linux case; option (a) is what every tool eventually does but carries design cost.

3. **This is a #156-adjacent concern.** If #156 is specifying "production startup posture," log capture during startup failure is in its territory more than #153's. The installer is downstream of whatever #156 decides.

4. **Shipping without it is not a crisis.** The v0.1.0 audience is technical operators who know how to `collabhost > collabhost.log 2>&1` or install a systemd wrapper. The feature earns its place when the audience broadens.

**Recommended change:** §17.2 Q3 resolves to "out of #153 scope; file as follow-up card (size S, labels `Backend` + `Platform`) once #156 lands." Update INSTALL.md §9 Troubleshooting "Binary crashes before I see anything" to say "redirect stdout/stderr to a file via `collabhost > collabhost.log 2>&1` on Linux/macOS; on Windows, `collabhost.exe > collabhost.log 2>&1` in PowerShell." One-line operator workaround, costs nothing, removes the shipping dependency.

---

## 7. Anything Else Bill Should Know

### 7.1 `Program.cs:18` is a pre-existing defect, not an R2 regression

R2-C2 applies to existing code, not code Remy is introducing. It happens to matter now because §2.5 formalizes a precedence chain the current code doesn't honor in dev. If Bill wants this addressed as a separate fix (not in #153), that's fine -- but then §2.5 should document the dev exception so it doesn't set a false expectation.

### 7.2 `InternalsVisibleTo` + `AssemblyName` override -- §17.2 Q9 has no ripple

Verified directly. `Collabhost.Api.csproj` declares `<InternalsVisibleTo Include="Collabhost.Api.Tests" />`. IVT works by **friend-assembly name**, not by the declaring assembly's output name. The friend is `Collabhost.Api.Tests`, which is determined by the test csproj's assembly name (unchanged). When the API csproj's publish step overrides `AssemblyName=collabhost`, the compiled DLL's name on disk changes to `collabhost.dll`, but the IVT attribute's target is still `Collabhost.Api.Tests` and still matches at compile time.

Test builds run against the non-published output (where the assembly name is still `Collabhost.Api`). IVT is resolved at compile time against the friend assembly's name, which is stable.

**Q9 closes: no ripple.** Spec can collapse Q9 into §14.6 as verified, not open.

### 7.3 Settings audit files are referenced but not on this branch

§12 cites `.agents/temp/settings-audit.md` and `.agents/temp/settings-audit-shipped.md`. Those files are not present on `spec/153-release-pipeline-r2`. I verified the #156 card comment corroborates the findings (F1, `Platform:ToolsDirectory` dead config) so the R2 text is trustworthy without the audit being checked in -- but for a reviewer landing cold on this branch six months from now, the reference will read as a broken link. Either commit the audit files to `.agents/temp/` on the r2 branch, or update §12's citation to quote the #156 card comment as the source-of-truth.

### 7.4 Phase 3 without Phase 2 ships a partial precedence chain for Caddy

§18 Phase 3 introduces `COLLABHOST_CADDY_PATH` but defers the full `CaddyResolver` to Phase 2. Between Phase 3 landing and Phase 2 landing, the runtime behavior is:

- `COLLABHOST_CADDY_PATH` env var: honored (new)
- `Proxy:BinaryPath` config: honored (existing)
- Bundled sidecar: **not honored yet** -- there's no sidecar in dev builds anyway

So the intermediate state is: env var > config > (no bundled fallback). That's harmless because Phase 3 lands before there's a production archive with a bundled Caddy, and dev installs still have `Proxy:BinaryPath = "caddy"` working. But the spec should name this explicitly in Phase 3's "scope" subsection to avoid surprise when Phase 2 implementer asks "wait, did Phase 3 already add the bundled fallback?"

Minor. Fold into the one-line clarification I asked for in §3.1 above.

### 7.5 `ProxySettings` singleton + `AdminPort` mutation is unrelated but visible

§R2-C6 flagged the `required` relaxation cascade. While I was there I noticed `ProxySettings.AdminPort` is a settable `int` (line 18) that `_Registration.cs:21` mutates before DI registration. That's the existing code pattern; `ProxySettings` is a singleton so the mutation is fine. But: a settings class with a non-`init` setter is a smell -- the "settings" abstraction implies immutability, and the port allocation is runtime state that belongs somewhere else (on `ProxyManager` or a new `ProxyRuntime` singleton). Not in scope for #153; filing as an observation.

**Anomaly: `ProxySettings.AdminPort` has a settable `int` property mutated at DI registration time.** Conflates settings with runtime state. Out of #153 scope; file when the pattern becomes load-bearing elsewhere.

---

## 8. Verification summary

| Item | Status |
|------|--------|
| Read spec in full (`release-pipeline.md`, 1668 lines) | ✓ |
| Read R1 review in full | ✓ |
| Read card #159 description + 3 comments (Q1-Q6 closed) | ✓ |
| Read card #156 description + Nolan's F1 comment (admin key 3-scenario model, dead `Platform:ToolsDirectory`) | ✓ |
| Read card #158 description + admin-key-UX research comment | ✓ |
| Verified `InternalsVisibleTo` + `AssemblyName` override has no ripple | ✓ |
| Verified `Program.cs:18` loads `appsettings.Local.json` post-CreateBuilder (R2-C2) | ✓ |
| Verified `CaddyClient.cs` has 3 blanket catches, not 1 (R2-C8 / §3.3) | ✓ |
| Verified `ProxyManager.CurrentState` has no existing memory model (R2-C4) | ✓ |
| Verified `ProxySettings` has 5 `required` fields, only `BinaryPath` loses `required` (R2-C6) | ✓ |
| Settings audit files (§12 citations) not on r2 branch -- noted as §7.3 | ✓ |

---

## 9. Summary of recommended changes

**Ranked.** Items 1-5 are Phase 2/3 prerequisites. Items 6-10 are spec-polish before merge. Items 11-12 are operator-contract decisions for Bill.

1. **Lock `proxyState` to a 5-state C# enum** (`starting | running | failed | disabled | stopped`), document concurrency model (`volatile` field or `Interlocked`), close §17.2 Q6. [§3.2(a), (b), (c); R2-C3, R2-C4]

2. **Narrow all three `CaddyClient` blanket catches in Phase 2**, not just `IsReadyAsync`. Update §6.5 row. [§3.3]

3. **Change `ProxySettings.BinaryPath` to `string BinaryPath { get; init; } = "";`** rather than nullable. Name this in §6.5. [R2-C6]

4. **Remove the 2-second sleep in `ProxyManager.ProcessSyncRequestsAsync`** in Phase 2, since `VerifyCaddyReady` replaces its purpose. [R2-C7]

5. **Decide on dev precedence chain**: re-add `AddEnvironmentVariables()` after `AddJsonFile("appsettings.Local.json")` (Option B), or document the dev exception in §2.5 (Option C). My preference: Option B. [R2-C2]

6. **Phase 3 scope clarification**: add a one-line note that Phase 3 introduces a partial Caddy resolver (env var honored, bundled not yet honored); the full resolver lands in Phase 2. [§3.1, §7.4]

7. **Q9 `InternalsVisibleTo` verified -- no ripple.** Close in §14.6. [§7.2]

8. **Close §17.2 Q2** (Caddy download checksum): "yes, included in Phase 4." [R2-C5]

9. **Close §17.2 Q3** (log directory): out of #153 scope, file follow-up card, INSTALL.md uses the shell-redirect workaround. [§6]

10. **Commit settings-audit files to the r2 branch** or replace §12's citation with the #156 card-comment reference. [§7.3]

11. **Q7 `appsettings.json` reinstall behavior**: my recommendation is Option C (overwrite + INSTALL.md education + ship more `COLLABHOST_PROXY_*` env vars). Needs Bill's ruling. [§5]

12. **Phase 4 / #156 coupling**: either gate Phase 4 on `.agents/specs/production-startup.md` existing (not just #156 merged), or split Phase 4 into 4a (workflow, only depends on Phases 1-3) + 4b (installer + INSTALL.md, depends on #156). My preference: 4a/4b split. Needs Bill's ruling. [R2-C1]

---

## 10. Anomalies / codebase observations

Carrying forward from R1, adding R2 findings:

- **R1 carry: `Program.cs:18` loads `appsettings.Local.json` post-CreateBuilder.** In dev, this violates the §2.5 locked precedence (`.Local.json` overrides env vars). Fix in Phase 3 (Option B from R2-C2) or document the dev exception.
- **R1 carry: `CaddyClient` has three blanket `catch (Exception)` blocks.** Narrow all three in Phase 2.
- **R1 carry: `ProxyManager.StartAsync` stale log message** (§17.4 on spec, still open). Update copy in Phase 2.
- **R1 carry: `TypeStore` file-watcher behavior change in production** (§17.4 on spec). Owned by #156, flagged for tracking.
- **R2 new: `ProxySettings.AdminPort` is a settable `int` mutated at DI registration.** Conflates settings with runtime state. Out of #153 scope. File as follow-up if/when the pattern is touched elsewhere.
- **R2 new: `ProxyManager.CurrentState` (new property) needs explicit concurrency model.** Not an existing bug -- the field doesn't exist yet. But naming the memory model is part of Phase 2's design.

---

*Review complete. 1668-line spec read in full; 5 code files read in full; 3 cards read in full; R1 review cross-referenced. No code changes. Single deliverable: this file.*
