# #153 Phase 2 -- PR #91 Architectural Review (Marcus)

**Branch:** `feature/153-02-caddy-resolver` @ `8c8e4e8`
**Reviewer:** Marcus
**Date:** 2026-04-20
**Scope:** `CaddyResolver`, `ProxyState`, `ProxyManager` post-launch probe + state machine, `CaddyClient` catch-narrowing, `proxyState` wire contract.
**Out of scope:** Dashboard UI (Dana), `IsDevelopment()` gate lifts (#156), Probe A (dropped), `COLLABHOST_CADDY_ADOPT_EXISTING` (deferred), Phase 4a/4b/5.

---

## Verdict

**Ship-with-fixes.** The core architecture is right -- precedence chain, soft-fail-with-visibility, narrowed catches all land cleanly. Two real issues in the state machine need to be addressed before merge (HIGH-1 restart-to-Starting gap, MED-1 late-write race in `ProbeAndActivateAsync`). Several MED items are lower-stakes hygiene. Nothing here is foundational; all fixes are local to `ProxyManager.cs`.

---

## Strong positives

1. **Precedence chain is correct and pure.** `CaddyResolver.Resolve` is a pure static with only `File.Exists` + env-var reads + a subprocess for `where`/`which`. Env > config > bundled is the order locked in R2 review -- implemented literally, with fall-through warnings at each miss (lines 26-31, 45-49). No Probe A residue, no `localhost:2019` lookup, no silent adoption.

2. **`IsNullOrWhiteSpace` guards on both env var and config path.** The bug mode I worried about in R2 -- trailing whitespace in a wrapper script silently "winning" the precedence chain -- is defended. Tests cover the whitespace case explicitly (`Resolve_EnvVarWhitespace_FallsThroughToConfig`, `Resolve_ConfigBinaryPathWhitespace_FallsThroughToBundled`).

3. **Catch narrowing is right, and the extension beyond §6.5's literal scope is defensible.** R2 lesson was "cite-one, check-all." Remy narrowed all three HTTP sites (`IsReadyAsync`, `LoadConfigAsync`, `GetConfigAsync`) to `HttpRequestException` + `TaskCanceledException` with the `!ct.IsCancellationRequested` guard. That guard is load-bearing -- it lets caller cancellations propagate instead of being mis-categorized as timeouts. Not scope creep; the three catches were copy-paste coherent, and narrowing one while leaving two would have been the worse outcome (my R1 catch-narrowing rationale applies identically to all three).

4. **5-state enum with default `Starting` is implemented as specified.** `ProxyState.Starting = 0` ensures field-default reads as `Starting`, so queries during boot never see `null`/`unknown`. Lowercase-at-boundary via `ToString().ToLowerInvariant()` keeps the wire shape stable while the rest of the codebase stays type-safe.

5. **`proxyState` as the visibility contract.** `/api/v1/status` now carries `proxyState` (`SystemStatus` record + endpoint). The integration test (`SystemStatusProxyStateTests`) asserts presence + all five lowercase wire values. This is the §6.4.2 contract my R2 pushed for.

6. **Post-launch probe loop matches §6.4.2.** 5s overall deadline, 1s per-attempt timeout via linked CTS, 200ms polling, early-exit on caller cancel. `VerifyCaddyReadyAsync_CallerCancelled_ReturnsFalseFast` confirms caller cancellation shortcircuits.

7. **`ProxyAppSeeder` soft-fails cleanly on null resolution.** No throw, no partial seed, explicit warning log referencing the precedence chain and the remediation path. The seeder's pre-Phase-2 TODO/pragma is gone.

8. **Concurrency posture documented at the declaration site.** Comment at `ProxyManager.cs:60-63` explains the volatile choice + the specific invariant it defends (visibility ordering, non-torn presentation). This is the kind of load-bearing comment that should live on a field, not in a PR description.

---

## Issues

### HIGH-1 -- State machine does not re-enter `Starting` on a restart from `Stopped`/`Failed`

**`ProxyManager.cs:296-333`** (`HandleProxyAppStateChange`)

Writers to `_currentState` are: the field initializer (`Starting`), `StartAsync` disabled path (`Disabled`), the event handler (`Failed`, `Failed`, `Stopped`), and `ProbeAndActivateAsync` (`Running`, `Failed`). **Nothing ever writes `Starting` after the field initializer.**

Sequence that exposes the gap:
1. Operator stops the proxy. Event bus fires `Stopped`. Handler sets `_currentState = Stopped`. `/status` reports `stopped`. ✓
2. Operator restarts the proxy. Supervisor emits `ProcessState.Starting`. Handler's switch case is an explicit no-op (line 327-331). `_currentState` remains `Stopped`.
3. Supervisor emits `ProcessState.Running`. Handler schedules `ProbeAndActivateAsync`. Probe is in flight for up to 5s.
4. During the probe window, `/status` still reports `stopped` (or `failed` if that's where we came from). The dashboard shows a "dead" proxy that is in fact probing.
5. Probe completes -> `Running` or `Failed`.

The visibility contract (§6.4.2) is: `starting` means "probe in progress, ≤5s." On a restart we violate it: we report the previous terminal state for the full probe window.

**Fix:** Add a `Starting` transition at the front of `ProbeAndActivateAsync` (before `VerifyCaddyReadyAsync`), or -- cleaner -- write `_currentState = Starting` in the `ProcessState.Running` case before the fire-and-forget dispatch. The second is preferable because it's co-located with the event that triggers the probe.

```csharp
case ProcessState.Running:
    _currentState = ProxyState.Starting;  // enter probe window
    _logger.LogInformation(...);
    Task.Run(() => ProbeAndActivateAsync(...));
    break;
```

This also fixes the initial-boot case if the proxy process is slow to come up: today if the supervisor is mid-`Starting` when `/status` is first hit, the field default works, but only because `ProbeAndActivateAsync` hasn't yet transitioned us anywhere.

A companion consideration: should `_proxyDisabled` be cleared on restart? Today once a `Fatal` event has set it `true`, no subsequent `Running` event will re-enable sync. That may be correct-by-design (`_proxyDisabled` == "this Collabhost process has given up on the proxy"), but it's worth making explicit. If the supervisor eventually surfaces a "proxy was reset from outside" event, we'd want a path to clear it. Not a blocker for #153; call out in §17.4 as a standing item.

---

### MED-1 -- Late-write race in `ProbeAndActivateAsync` can mask a crash as `Running`

**`ProxyManager.cs:335-366`** (`ProbeAndActivateAsync`) + **`:296-333`** (`HandleProxyAppStateChange`)

The two paths both write `_currentState`. `ProbeAndActivateAsync` runs on a `Task.Run` thread; `HandleProxyAppStateChange` runs on whatever thread the event bus dispatches on. Interleaving scenario:

1. Proxy supervisor emits `Running`. Handler fires off probe.
2. Probe's first `IsReadyAsync` call returns `true` (Caddy responded).
3. Before line 343 executes (`_currentState = Running`), Caddy crashes.
4. Supervisor emits `Crashed`. Handler line 310 runs: `_currentState = Failed`.
5. Probe resumes line 343: `_currentState = Running`.

Final state: `/status` reports `running` while Caddy is dead. The crash event is silently overwritten by a stale success.

**Probability:** low in practice (needs Caddy to answer once then crash within ~100µs, which would typically mean Caddy is misbehaving on startup). **Impact:** breaks the soft-fail-with-visibility contract precisely when operators most need it -- Caddy that flapped, not Caddy that never came up.

**Fix options, simplest first:**

- **(a) Compare-and-swap the `Running` write.** Only transition to `Running` from `Starting`:

  ```csharp
  if (Interlocked.CompareExchange(ref _currentState, ProxyState.Running, ProxyState.Starting)
      != ProxyState.Starting)
  {
      _logger.LogInformation("Probe succeeded but another transition won the race; leaving state as {State}", _currentState);
      return;
  }
  ```

  Note: `Interlocked.CompareExchange` doesn't take a `volatile` field by ref. Either drop the `volatile` modifier and rely solely on Interlocked (preferred -- the spec called `volatile` OR `Interlocked` as acceptable), or use `Interlocked.CompareExchange(ref Unsafe.As<ProxyState, int>(ref _currentState), ...)` with casts. The cleaner refactor is to drop `volatile` and use `Interlocked` for every write.

- **(b) Same CAS for the `Failed` write in the soft-fail path.** Only transition to `Failed` from `Starting`. If the handler already moved us to `Failed`/`Stopped`, leave it.

- **(c) Sequence guarantee.** Grab a `long _transitionSequence` counter incremented on each event-handler write; the probe captures the sequence before its first attempt and aborts the final write if the counter has advanced. More machinery than necessary for this scale.

I'd take **(a)+(b)** -- convert both `ProbeAndActivateAsync` writes to CAS-from-`Starting`, and keep the event handler as "latest event wins." This matches the semantic: the probe can only transition *into or out of* the startup window.

---

### MED-2 -- Unobserved task exception on fire-and-forget probe

**`ProxyManager.cs:155`** and **`:305`**

Both fire-and-forgets run `Task.Run(() => ProbeAndActivateAsync(...))`. `ProbeAndActivateAsync` catches `OperationCanceledException` only. Any other exception (NRE in logger/caddy-client, socket-layer surprise, MA-analyzer-not-covered case) escapes into the Task. Unobserved tasks in .NET don't crash the process since .NET 4.5, but the exception is silently lost and the state machine is left in whatever partial state it reached.

**Fix:** Wrap the probe body in a broader `catch (Exception ex)` *after* the `OperationCanceledException` handler, log at `Error`, and transition to `Failed` + `_proxyDisabled=true` (the most defensive terminal). Also sets us up for the MED-1 CAS: the outer catch is another write-site that needs CAS.

```csharp
catch (OperationCanceledException)
{
    // expected during shutdown
}
catch (Exception ex)
{
    _currentState = ProxyState.Failed;
    _proxyDisabled = true;
    _logger.LogError(ex, "Proxy probe aborted unexpectedly -- subsystem disabled");
}
```

Applies to both call sites -- same probe, same exception surface.

---

### MED-3 -- `ResolveFromPath` has no timeout on the `where`/`which` subprocess

**`CaddyResolver.cs:84-122`**

`process.WaitForExit()` with no timeout. Under normal conditions `where` and `which` complete in milliseconds. Under pathological conditions (system under load, antivirus interposing, a broken `%PATH%` with unreachable network drives on Windows) this hangs indefinitely. Because `Resolve` is called during `ProxyAppSeeder.SeedAsync` -- which is on the API startup path -- a hung subprocess blocks Collabhost from booting.

**Fix:** `process.WaitForExit(TimeSpan.FromSeconds(2))`; if it returns false, `process.Kill()` and return null. 2s is generous for a PATH lookup.

The blanket `catch (Exception)` at line 116 would catch a `Kill`-induced exception, so the safety net is already there for the kill path.

---

### MED-4 -- Concurrency stress test skipped; spec §15.1 listed it explicitly

**`.agents/specs/release-pipeline.md:1375`** lists `CurrentState_ConcurrentReadWrite_DoesNotTear` as a required unit test. Remy's self-report says he skipped it on the theory that "`volatile` + enum-atomic-read is the correctness argument." That's true *at the CLR-memory-model level*, but:

1. MED-1 above shows the race isn't really about torn reads -- it's about **ordering between independent writers**, which `volatile` does not address.
2. The spec called the stress test "recommended but not mandatory" in one spot and included it in the §15.1 test list in another. Ambiguous; default to the explicit list.
3. The test as written in §15.1 would likely pass today (enum-atomic reads don't tear on aligned 32-bit integers), so skipping it doesn't catch MED-1 either. The right test for MED-1 is a **directed test** (two threads, one writing via event handler, one writing via probe, assert final state is the expected deterministic one for a given interleave).

**Recommendation:** add a directed test that simulates the MED-1 race after the CAS fix lands. The pure torn-read stress test has low coverage value; the directed race test catches the actual bug.

---

### MED-5 -- `VerifyCaddyReady_NeverReady_ReturnsFalseAfter5s` doesn't assert downstream state

**`backend/Collabhost.Api.Tests/Proxy/ProxyManagerVerifyCaddyReadyTests.cs:60-74`**

§15.1 names this test `VerifyCaddyReady_NeverReady_ReturnsFalseAndProxyStateFailed` -- the contract was to assert `ProxyManager.CurrentState == ProxyState.Failed` after the probe completes. Current test only asserts `result.ShouldBeFalse()`. The state transition happens in `ProbeAndActivateAsync`, not `VerifyCaddyReadyAsync`, so the literal assertion is harder -- but an equivalent-level test that invokes the probe-and-activate flow and asserts the `Failed` state + `_proxyDisabled=true` side-effects is straightforward to add.

Not a blocker; would close a real gap in the soft-fail behavior coverage.

---

### LOW-1 -- Relative path handling in `BinaryPath` is unspecified

**`CaddyResolver.cs:74-78`**

`ResolveBinaryPathSetting` treats any string containing a separator as a path, then uses `File.Exists` + `Path.GetFullPath`. For `"./caddy"` or `"../bin/caddy"`, `File.Exists` resolves against `Environment.CurrentDirectory`. For an ASP.NET Core process, CWD is often the project directory in dev but may be something else in production (Windows Service, systemd unit, launchd agent).

The spec says "Absolute path -> used as-is. Bare name -> PATH resolution." Relative-path-with-separator isn't called out. I'd recommend one of:

- Document that relative paths are allowed and resolved against `AppContext.BaseDirectory` (more predictable than CWD).
- Reject relative-with-separator as an explicit warning ("use absolute path or bare name").

Either is fine; today's behavior (CWD-relative) is surprise-prone.

---

### LOW-2 -- Dispose-during-probe race

**`ProxyManager.cs:507-519`**

`Dispose()` calls `_shutdownCancellation?.Dispose()`. Under the normal `IHostedService` lifecycle `StopAsync` runs first and awaits the processor task, but `ProbeAndActivateAsync` is fire-and-forget -- `StopAsync` doesn't await it. If the host tears down a Dispose before the probe has exited its 5s loop, the probe references a disposed CTS token (via `VerifyCaddyReadyAsync`'s linked CTS).

Practically this would surface as an `ObjectDisposedException` inside the `Task.Run` (unobserved per MED-2). Low stakes; fix subsumed by MED-2's broader catch, or by tracking the probe task and awaiting it in `StopAsync`.

---

### LOW-3 -- `CaddyResolver.Resolve` takes non-generic `ILogger`

**`CaddyResolver.cs:11`**

Project convention (dotnet-dev skill: "Use `ILogger<T>` with the concrete generic -- never widen to `ILogger`") is generic-typed. `CaddyResolver` is a static class, so `ILogger<CaddyResolver>` would be the right choice. Current implementation takes `ILogger` and lets callers pass whatever (today: `ILogger<ProxyAppSeeder>`). Log source categories will read as `ProxyAppSeeder`, not `CaddyResolver`, which mis-attributes the fall-through warnings.

Low-stakes; fix is a signature change + callers pass `loggerFactory.CreateLogger<CaddyResolver>()`. Or just leave it -- static functions with injected loggers are one of the places the convention is genuinely fuzzy.

---

### LOW-4 -- xUnit env-var tests mutate process-wide state

**`CaddyResolverTests.cs:34, 54, 71, 93, ...`**

`Environment.SetEnvironmentVariable` mutates the process. If xUnit ever parallelises the `CaddyResolverTests` class across other tests that read `COLLABHOST_CADDY_PATH`, you'll get flakes. Today, nothing else reads that variable, so this is quiet. Note worth carrying.

**Fix if it ever flakes:** `[Collection("CaddyResolverEnvVars")]` on the class. Not needed today.

---

## Questions for Remy

None blocking. The state-machine items (HIGH-1, MED-1) are clear enough to fix without discussion; happy to pair on the CAS pattern if useful, but the shape is in the suggestions above.

Worth confirming before merge: **is the `_proxyDisabled` latch intentionally one-way for the process lifetime, or should `Running` event on a reborn Caddy clear it?** I lean "intentionally one-way, this process gave up" -- but if you disagree, argue the other side and we can add a targeted clearing path.

---

## Convention violations

- `ILogger` vs `ILogger<T>` at `CaddyResolver.Resolve` (LOW-3 above).
- Nothing else -- build is clean (0 warnings), `dotnet format --verify-no-changes` exit 0.

---

## Scope notes

- **Catch-narrowing extension to `LoadConfigAsync` / `GetConfigAsync` was not literal §6.5 scope.** Defensible; flagged transparently; I support the extension on the R2 "cite-one, check-all" principle. No carding needed.
- **Concurrency stress test deliberately dropped.** Acceptable given MED-4's recommendation to swap it for a directed race test after MED-1 fix lands.
- **Manual smoke on the full probe path not run.** Dashboard smoke is Dana's. Recommend a single local `aspire start` pass post-fix to eyeball the `starting -> running` transition live, before the branch merges. Not a gate, just a sanity step.

---

## Summary of required changes before merge

1. **HIGH-1:** write `_currentState = Starting` in the `ProcessState.Running` case before `Task.Run(...ProbeAndActivate...)`.
2. **MED-1:** convert `ProbeAndActivateAsync`'s `Running` and `Failed` writes to CAS-from-`Starting` via `Interlocked.CompareExchange` (and switch the field from `volatile` to the Interlocked-only pattern, per spec §6.4.2 "either/or").
3. **MED-2:** add an outer `catch (Exception ex)` in `ProbeAndActivateAsync` that logs + transitions to `Failed`/`_proxyDisabled`.
4. **MED-3:** add a 2s `WaitForExit` timeout in `CaddyResolver.ResolveFromPath`.
5. **MED-4/5:** add a directed race test + tighten `ProxyManagerVerifyCaddyReadyTests.VerifyCaddyReady_NeverReady_*` to assert the downstream `Failed` + `_proxyDisabled` transitions via the probe-and-activate flow.

LOW items are polish; ship with or without, at Remy's discretion.

---

Co-Authored-By: Marcus <marcus@collabot.dev>
