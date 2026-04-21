# Marcus -- Contract-fidelity review of PR #96 (#156.2 gate lift)

**Reviewer:** Marcus
**PR:** [#96](https://github.com/MrBildo/collabhost/pull/96) `feature/156-02-gate-lift` @ `dccca54` -> `main` @ `0161f34`
**Spec:** `.agents/specs/production-startup.md` (committed, R2.1)
**Predecessor reviews:** `marcus-91-review.md` (#153 Phase 2), `marcus-92-review.md` (#153 Phase 4a), `marcus-95-review.md` (#156.1)
**Session:** fourth review of the day.

---

## Verdict: **ship-with-nits.**

No HIGHs. No MEDs. Five LOWs and two spec-alignment notes to fold into the consolidated spec-reconciliation PR later. The implementation hits the §8 / §9 / §14 contract cleanly, the exit-code taxonomy is wired correctly at every return site, and the scope fence (no §11, no INSTALL.md, no frontend, no installer) is observed. This is the cleanest of Remy's four implementations this session. Green-light to merge after the LOWs are weighed and (at Remy's / Bill's discretion) folded in now or rolled forward.

---

## Seam ruling -- user-types-dir creation stays in `StartupPreflight`

**Ratify preflight placement. No relocation requested.**

The seam question was genuinely unresolved in the dispatch trail: my R1 framing said "TypeStore owns the load semantics; put creation at the TypeStore init seam," but the committed SD-1 reconciliation (`a0f0c4f`) and spec §5.2 item 4 both put it in preflight with a pointer to §8.3/§8.5. Nolan's dispatch prompt echoed my earlier deferral language ("NOT in preflight") -- which was stale by the time #156.2 was cut. Remy followed the **committed spec**, which is the right call when dispatch and spec diverge.

Arguments for preflight (which I now endorse):

1. **Grouping.** Preflight already creates `data/` and `data/backups/`. User-types-dir is the same class of concern (filesystem prep before anything touches it). Putting it elsewhere would split a coherent group.
2. **Closes the silence gap called out in §8.3.** `TypeStore.StartWatching` silently skips if the directory doesn't exist. The spec explicitly flagged that silence as the problem -- "this feature is disabled without the operator ever knowing the feature existed." Creating in preflight with the informational `"User-types directory initialized at {Directory} -- drop *.json files here to register custom app types"` log on first creation is exactly the affordance §8.3 asked for. It has to happen *before* `StartWatching` observes the path, and preflight is the earliest seam where that's true.
3. **The `ResolveEffectiveUserTypesDirectory` helper exists precisely to make this work.** Remy added a public static function on `TypeStoreRegistration` that mirrors `TypeStore.ResolveUserTypesDirectory`'s path composition, so preflight and runtime resolve to the same path. That is the right layering: settings-resolution logic lives with TypeStore, filesystem prep calls into it. If the creation lived inside TypeStore, the helper wouldn't be needed -- but neither would the log-on-creation surface, which is the whole point of the change.

Arguments I considered for TypeStore-seam relocation and rejected:

- **"Creating a dir TypeStore hasn't read yet is meaningless."** True in a narrow sense, but the log *is* the operator affordance, and the log needs to fire at a location the operator is watching (startup logs), not a location that is later used as a reload trigger (`StartWatching`). The dir being created early is a benign side effect.
- **"TypeStore should be the single owner of its filesystem surface."** Defensible if TypeStore owned the whole lifecycle, but `StartupPreflight` already touches filesystem surfaces other subsystems own (data dir for `DbContext`, backups dir for `MigrationRunner`). The pattern is "preflight preps, subsystems consume."

**Ruling closes the ambiguity for #156.3 and the spec-reconciliation doc.** No code change needed.

Spec-alignment note: §5.3 code seam still says `"Validate(dataDirectory, ILogger) -> PreflightResult"`. That signature is now stale -- the implementation correctly matches §5.2 item 4's broader scope, and the signature in §5.3 should be updated in reconciliation to `Validate(dataDirectory, userTypesDirectory, ILogger) -> PreflightResult`.

---

## Strong positives

1. **Exit-code discriminator is correct at both throw sites.** `TypeStore.LoadAsync` passes `isBuiltIn: true` at the built-in validation failure site (line 72) and `isBuiltIn: false` at the user-type site (line 100). Program.cs branches on `ex.IsBuiltIn` to pick 30 vs 31. Test `LoadAsync_UserTypeSlugConflictsWithBuiltIn_Throws` has a fresh assertion `exception.IsBuiltIn.ShouldBeFalse()` with a comment referencing §8.2 -- the expected behavior is pinned. The built-in failure path has no dedicated integration test, but Remy's self-reported rationale (cost > value for a packaging-bug fixture) holds; cost-to-value of that harness is poor and the discriminator is unit-tested implicitly by the user-type test.

2. **Exit-40 branch is tight.** `ProxyAppSeeder.SeedAsync` is wrapped in `try { ... } catch (Exception ex) { LogCritical + StartupStderr + return 40 }`. Catch-Exception at this level is correct for §9.4's "any unexpected throw halts startup." `CaddyResolver.Resolve` itself never throws (exceptions are caught internally); its soft-fail returns `null` and the seeder logs-and-returns, which means the seeder will NOT reach the throw-catch branch from a Caddy-absent case. The exit-40 halt is reserved for DB / transaction / truly unexpected failures, which is what §9.4 asked for.

3. **Program.cs sequence matches the §4 contract.** Comment annotations at each phase (`phase 6`, `phase 7`, `phase 9`) make the contract traceable in the code. Ordering is: Preflight (2) → Build → Migration+Backup (4+5) → TypeStore.LoadAsync (6) → ProxyAppSeeder.SeedAsync (7) → StartWatching (9) → ApplicationStarted.Register (sentinel) → middleware → Kestrel. Phase 8 (UserSeedService) is correctly absent; that's #156.3's territory. Phase ordering respects §4's dependencies: TypeStore-before-Seeder (seeder reads `system-service` type), LoadAsync-before-StartWatching (watcher needs a loaded snapshot to compare against).

4. **Spec §8.5's "no contract change on TypeStore.cs" is honored.** The only TypeStore changes are passing `isBuiltIn:` to the existing exception ctor. No behavior shift, no new public surface. The disruption is contained to `_Registration.cs` (new `ResolveEffectiveUserTypesDirectory` helper) and the exception's ctor signature.

5. **Dead-config cleanup is complete.** `git grep ToolsDirectory` on `feature/156-02-gate-lift` returns zero hits -- not in C#, not in JSON, not in tests, not in build files. The only remaining reference is the in-spec discussion at §14.2 (which stays, as historical record).

6. **Integration-test fixture adjustments are minimal and correct.**
   - `ApiFixture`: 21 lines added for an isolated per-test user-types directory + cleanup. The reason -- "we want a clean slate so test runs don't leave a stray 'UserTypes' dir next to the test binaries" -- is both real and well-comment-justified. Without this, post-#156.2 integration tests would create a `UserTypes` dir in the Test binaries output folder on every run.
   - `AppHostFixture`: 24 lines for the same reason, but using `COLLABHOST_USER_TYPES_PATH` env var (correct, because the AppHost spawns the API as a child process that reads env vars, not `UseSetting`). Also correctly nulls the env var in `DisposeAsync`.
   - No expansion into peripheral test surfaces. Targeted.

7. **Self-reported scope-fence discipline.** The PR body explicitly flags the dispatch-vs-spec drift on SD-1 and names what Remy chose to follow (spec). This is exactly the disclosure hygiene I flagged positively on #156.1 -- tell the reader where your implementation diverged from a source of truth, and which source you followed. Makes the review efficient.

---

## Issues

### HIGH
None.

### MED
None.

### LOW

**LOW-1 -- `ApiFixture` doesn't null `COLLABHOST_USER_TYPES_PATH` before calling `UseSetting`.**
*File:* `backend/Collabhost.Api.Tests/Fixtures/ApiFixture.cs`, `InitializeAsync`.

The ApiFixture sets `builder.UseSetting("TypeStore:UserTypesDirectory", _userTypesDirectory)` but does not null-out the `COLLABHOST_USER_TYPES_PATH` env var. Per `TypeStoreRegistration.ResolveSettings`, env var wins over config. If a developer has the env var set in their shell (common if they were debugging production-startup behavior) every test will silently point at the developer's local path, not the per-test temp dir.

The AppHostFixture takes the opposite approach -- it sets the env var intentionally and nulls it on dispose -- which is necessary because the child process needs env vars. But the in-proc ApiFixture should either null the env var at fixture-init time (and re-set at dispose) or prefer the env-var path too, for symmetry.

**Fix (one line):** at the top of `InitializeAsync`, `Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null)` with a corresponding note at `DisposeAsync`. Cheap guardrail, no behavioral change on clean environments.

**LOW-2 -- `StartupPreflight.TryEnsureDirectory` doesn't catch `ArgumentException` or `NotSupportedException` from malformed paths.**
*File:* `backend/Collabhost.Api/Platform/StartupPreflight.cs`, lines 121-139.

If `COLLABHOST_USER_TYPES_PATH` is set to a string with invalid path characters (e.g., `<>|` on Windows), `Directory.CreateDirectory` throws `ArgumentException` or `NotSupportedException`, not `IOException`. These escape `TryEnsureDirectory` and propagate past Program.cs's preflight guard, halting startup with an ASP.NET Core generic unhandled-exception path (exit 1) instead of a clean exit 10 + the stderr shape §12 requires.

This is a pre-existing risk on the `dataDirectory` parameter too, surfaced by #156.1 but not regressed by #156.2. The failure mode is narrow (operator sets a malformed override path), but the spec's §12 "every halt exits via a single place" contract is broken. Safe to fold into the spec-reconciliation PR or leave for a future polish pass. Not blocking.

**Fix (trivial):** add `ArgumentException` and `NotSupportedException` to the catch list in `TryEnsureDirectory`.

**LOW-3 -- `TypeStoreRegistration.ResolveSettings` is called twice during startup.**
*File:* `backend/Collabhost.Api/Program.cs` line 39 + `_Registration.cs:AddTypeStore` line 54.

`Program.cs:39` calls `ResolveEffectiveUserTypesDirectory(builder.Configuration)`, which calls `ResolveSettings` internally. `AddTypeStore(builder.Configuration)` also calls `ResolveSettings`. The env-var read happens twice, the config lookup happens twice. Same inputs produce same outputs; the redundancy is cosmetic. The only practical risk is if someone mutated the env var between the two calls (no one does), or if the `ResolveSettings` grew non-pure behavior (future worry, not current).

**Fix (if we care):** compute `TypeStoreSettings` once in Program.cs, pass it both to `StartupPreflight.Validate` (via a path composition) and to `AddTypeStore` (via an overload). Net delta: ~5 lines. Not worth the surface change if nothing else forces it.

**LOW-4 -- TypeStore exception `IsBuiltIn` comment documents "exit 30 / exit 31" but the exit-code policy lives in `Program.cs`.**
*File:* `backend/Collabhost.Api/Data/AppTypes/_Registration.cs`, lines 36-38.

The comment on `IsBuiltIn` says "true when the failing validation is on the embedded built-in types (packaging bug, exit 30)" -- which binds a data classification to a specific caller's policy. If a different caller wants a different exit code (e.g., a future `collabhost validate` CLI subcommand that prints errors but doesn't exit), the comment suggests the mapping is invariant.

Minor: the comment would be more honest as "built-in vs user-type classification; Program.cs maps to exit 30 vs 31 at startup." The `See §8.2 of the production-startup spec` pointer already hedges this -- spec §8.2 is explicit that 30/31 is the Program.cs policy. Taste nit, not a correctness concern.

**LOW-5 -- `StartWatching` placement means middleware setup runs after FSW activation.**
*File:* `backend/Collabhost.Api/Program.cs`, line 278.

`TypeStore.StartWatching()` is called immediately before the dev-only `MapOpenApi + UseCors` block, before `ApplicationStarted.Register`, and before all middleware / endpoints are mapped. This is fine per §4 (StartWatching is phase 9, before Kestrel listens), but there's a ~few-millisecond window where an operator adding a JSON file would fire `OnFileChanged`, enqueue on the reload channel, and the channel's 500ms-debounce task would start a reload against a fully-loaded snapshot during the middleware wiring. Harmless in practice; the reload is `ReloadAsync`-robust (keeps current snapshot on error, swaps atomically on success). Noting for completeness.

If tightening is desired, move the `StartWatching()` call to `app.Lifetime.ApplicationStarted.Register(...)` so it fires after Kestrel listens. That matches §4 phase (9) "after (6)+(7)" literally but also adds a tiny delay between boot and watcher activation. Not blocking; current placement is defensible. Cross-reference for future sharpening only.

---

## Questions

**Q1 -- Is `COLLABHOST_USER_TYPES_PATH` precedence over `TypeStore:UserTypesDirectory` documented end-to-end?**

`TypeStoreRegistration.ResolveSettings` (line 70-76) implements `env > config > hardcoded default ("UserTypes")`. Spec §8.4 says "Already covered by #153 §12.3. Precedence `env > config > default`." #153 §12.3 was the source of truth; I'd want to confirm it still says what it said at the time #156 was written (unchanged unless the spec-reconciliation work touched it). The code matches what I remember reading. Cosmetic; no action unless a discrepancy surfaces.

**Q2 -- Does the `"(not resolved)"` fallback in the preflight-ok log ever fire in production?**

`Program.cs:39` unconditionally calls `ResolveEffectiveUserTypesDirectory` (which falls through to `Path.Combine(AppContext.BaseDirectory, "UserTypes")` if no env + no config) and passes that to preflight. So `userTypesDirectory` is never null in Program.cs's call path. The `"(not resolved)"` branch exists for callers that pass `userTypesDirectory: null` (e.g., the `StartupPreflightTests.Validate_NullUserTypesDirectory_SkipsUserTypesCheck` test). Fine; defensive design. Just noting that in the production boot path, the log message will always include a concrete path.

**Q3 -- Should the first-boot log be an explicit "first-boot" signal rather than the current conditional log?**

Current behavior: if the directory *exists at preflight time*, no "initialized" log fires. If it was created, the "drop *.json files here" informational log fires. On a second boot (dir already exists from first boot), the log is silent. This matches the spec's §8.3 framing ("silence is wrong *only* when the feature appears disabled"), but the operator running `collabhost` for the first time on a second boot (e.g., after a data-dir restore) would *not* see the affordance. Low-stakes; operators find the directory quickly via logs / INSTALL.md / probing the dashboard. Noting for completeness.

---

## Convention violations

None. Code style is clean:

- Allman parens on multi-arg calls (`StartupStderr.Write`, the new `StartupPreflight.Validate` signature, helper methods).
- Primary-constructor DI with `?? throw` captures (unchanged from #156.1).
- `file`-scoped helper not used; correct, because `BuildTypeStoreErrorDetails` is a static local at the bottom of `Program.cs` (consistent with the existing static-local pattern there).
- Tests use Shouldly with single-property assertions. Arrange-Act-Assert structure clean. Comments on `IsBuiltIn.ShouldBeFalse()` and on the per-test temp dir justify the non-obvious parts.
- No analyzer warnings introduced (PR body reports `0W/0E` on `--no-incremental`; I trust that given the diff size).

---

## Scope notes

- **No §11 admin-key touches.** Correct. `UserSeedService` remains a hosted service; the synchronous §11 call inline is #156.3.
- **No INSTALL.md.** Correct. Phase 4b owns INSTALL.md.
- **No frontend.** Correct.
- **No installer changes.** Correct.
- **No `ProxyAppSeeder` static `ResolveBinaryPath` removal.** Wait -- spec §9.5 says "Remove its static `ResolveBinaryPath` after #153 Phase 2 wires `CaddyResolver` through." I verified: `ProxyAppSeeder.cs` now calls `CaddyResolver.Resolve(...)` directly (line 54), and there's no static `ResolveBinaryPath` method left on `ProxyAppSeeder`. That removal happened with #153 Phase 2; it's already done by the time #156.2 lands, so no action needed. Cross-checking for completeness.

---

## Spec-reconciliation fold-ins

Consolidated-PR additions from this review:

1. **§5.3 code seam signature update.** `Validate(dataDirectory, ILogger)` → `Validate(dataDirectory, userTypesDirectory, ILogger)`. Aligns the seam description with §5.2 item 4 and the implementation.
2. **SD-1 closure confirmation.** The user-types-dir-in-preflight question is now fully resolved. §5.2 item 4 already has the pointer bullet committed in `a0f0c4f`. Implementation matches. No further reconciliation needed on SD-1 -- it graduates from "open" to "closed + referenceable."
3. *(No #156.2 net-new spec drift beyond §5.3.)*

Combined with the #153 + #156.1 items previously enumerated in `marcus-95-review.md`:

- #153 side: §6.2 SHA verify, §6.1 whitespace strip, §5.4 NOT-in-archive additions, §17.1 risks row, Appendix A, Implementation Plan post-hoc (7 items).
- #156 side: SD-1 pointer (landed, reference-only), SD-2 sentinel-read location (landed), SD-3 BackupsDirectory single-compute-site (landed), §5.3 signature update (new), UserSeedService-as-hosted-service clarification in §4 phase listing (future, part of #156.3).

I'll own the consolidated reconciliation pass once Phase 4a + #156.1 + #156.2 have all merged. Offer stands from `marcus-95-review.md`.

---

## One observation worth naming

This is the fourth review of the day on Remy's work, and the trend is sharpening. #153 Phase 2 was ship-with-fixes (HIGH + MED). #153 Phase 4a was ship-with-fixes (HIGH + MED). #156.1 was ship-with-fixes (no HIGHs, two MEDs). #156.2 is ship-with-nits (no HIGHs, no MEDs, five LOWs).

The trajectory is not an accident. #156.2 is smaller (+307 / -21) and better-scoped -- the gate-lift was clean-edged by design. But it's also the case that Remy's disclosure discipline is tightening: the PR body flagged the dispatch-vs-spec SD-1 drift explicitly rather than letting me find it, called out three gaps (manual smoke not done, no exit-30 dedicated test, no §16.3 E2E migration test), and pre-justified each with cost/value rationale I can accept or challenge.

That is the texture I'd name in a post-release retrospective: when the implementer flags their own soft spots before the reviewer has to, reviews compress. This session's review took me fewer passes through the diff than #156.1's did, and the review artifact is shorter -- not because the code is less complex, but because the disclosure surface is more honest.

The #156 sequence is tracking cleanly. Ball in Remy's / Bill's court on the LOWs.

-- Marcus
