# Marcus — PR #95 contract-fidelity review (#156.1 startup foundation)

**Branch:** `feature/156-01-startup-foundation` @ `0ec6ef4` off `add128a` (main).
**Scope:** #156.1 sub-phase of `.agents/specs/production-startup.md`. §4 sequence, §5 preflight, §6 backup, §6.2.1 sentinel, §7 migration posture, §12 stderr/exit codes, §13.1 upgrade story.
**Reviewer:** Marcus (spec author, R1 + R2).
**Verdict:** **ship-with-fixes.** No structural issues. Two MEDs, a handful of LOWs, one genuine spec-drift item on the user-types-dir question you flagged, and a couple of places where Remy's implementation drifted from the literal wording of §5 / §6.2.1 in ways that are either defensible or worth one tightening.

This is the third review-round pattern with Remy this session. PR #91 (Phase 2) and PR #92 (Phase 4a) were both ship-with-fixes. PR #95 is the cleanest of the three — the implementation matches the spec intent and where it deviates it's because the spec itself was mildly fuzzy in one or two places, not because Remy mis-read it.

---

## HIGH

None.

---

## MED

### MED-1 — Preflight skips §5.2 item 4 (read last-boot-version); timing drift is defensible but worth a spec-level acknowledgement

**What the spec says (§5.2 item 4):** "Last-booted version read. Reads `{dataDirectory}/.last-boot-version` per §6.2.1. Missing or malformed → `unknown`. Never halts."

**What the spec says (§5.3):** "New class `Platform/StartupPreflight.cs` with a single `ValidateAsync(IHostEnvironment, IConfiguration, ILogger) -> PreflightResult` method."

Plus §6.2.1: "Read during §5 preflight, cached in the startup context for the migration runner to consume."

**What Remy did:** `StartupPreflight.Validate` does NOT read the sentinel. The read happens at `Program.cs:115`, after `builder.Build()`, right before `MigrationRunner.MigrateWithBackupAsync`. The `PreflightResult` record doesn't carry the version.

**Assessment:** defensible in practice. The sentinel is only consumed by the migration runner; reading it just-in-time at the call site keeps `PreflightResult` clean (no "optional read-some-data-for-someone-else" field). The spec's "cached in the startup context" was a wave of the hand, not a contract.

**However:** the spec wording is explicit enough that this counts as spec-drift rather than a defensible simplification. Two options:

- **(a)** Move the read into `StartupPreflight.Validate` and add a `LastBootVersion` field to `PreflightResult`. Matches spec literally.
- **(b)** Leave the code as-is and fold §5.2 item 4 + §6.2.1 into a clarifying note in #156.2's docs PR: "the read is done at the migration call site; preflight no longer needs to know about it."

My read: **(b)** is cleaner. The preflight's job is validate-and-create-filesystem; the sentinel is purely a migration-runner input. Forcing it into the preflight result conflates two concerns. But it IS drift from what I wrote, so it should land in the spec reconciliation, not ship silently.

**Recommendation:** note this in the #156.2 follow-up doc commit. No code change here.

### MED-2 — `ApplicationStarted` is "post-host-started," not "post-Kestrel-listen," and in test harnesses it may fire in contexts the spec didn't model

**What the spec says (§6.2.1):** "Written at the end of successful startup (post-Kestrel-listen), atomically via write-to-temp + rename."

**What Remy did:** `app.Lifetime.ApplicationStarted.Register(() => BootVersionTracker.Write(...))` at `Program.cs:198-201`.

**Assessment:** `ApplicationStarted` fires after all hosted services' `StartAsync` completes AND Kestrel is listening. That matches "post-Kestrel-listen" for the production path. Good.

**However** — in the test harness (`ApiFixture`), `WebApplicationFactory<Program>` will trigger `ApplicationStarted` on every test. That means every integration test writes a `.last-boot-version` file to the test's temp data directory. That's harmless (the temp dir is deleted at test disposal) but it means the sentinel file is a side effect of test runs, not just production boots. Worth flagging because:

- A test that checks "the data directory is clean after tests" would now see the sentinel as noise.
- If a test ever runs against a long-lived data directory (e.g., CI cache), the sentinel accumulates.

Neither is a blocker. But it's a production-vs-test-harness coupling the spec didn't anticipate.

**Also:** `ApplicationStarted.Register` runs its callback synchronously on the host lifetime thread. `BootVersionTracker.Write` does `File.WriteAllText` + `File.Move` — both cheap but both synchronous disk I/O on a lifecycle callback. In practice it's fine (single ~30-byte write to local disk) but it's the kind of thing that could surprise on a slow filesystem. Not a fix, just a note.

**Recommendation:** no change. Mention in #156.2 test-harness note.

---

## LOW

### LOW-1 — Two separate `LoggerFactory.Create` instances in `Program.cs`

Lines 40-63 create a logger factory just for preflight output, and line 81 creates another for auth registration. Both are short-lived and disposed. It's not a correctness issue but it's a readability clutter in the top-level statements. A single shared early-logger factory would be cleaner. Not worth reshaping now.

### LOW-2 — `IsLockedException` message fallback is a belt-and-suspenders that'll match too much

```csharp
|| (ex.Message?.Contains("locked", StringComparison.OrdinalIgnoreCase) ?? false);
```

`SqliteErrorCode 5/6` (SQLITE_BUSY / SQLITE_LOCKED) are the right detection. The message contains-check adds tolerance for future SQLite versions or wrapped exceptions — but it also fires on any SqliteException whose message happens to contain "locked" for unrelated reasons ("the table is locked for a different reason"). Probably fine; the fallback is a defense against missing error codes at the cost of occasional false positives that still produce a reasonable exit code (11 — "stop any other Collabhost process"). Not a blocker; worth knowing.

### LOW-3 — `BootVersionTracker.Write` has no POSIX mode bits

Spec §6.2.1: "0600 on POSIX. Owned by the Collabhost process user."

`File.WriteAllText` on Linux creates the file with the process umask default (typically 0644). The spec called for 0600.

Small issue. Can be addressed either here or as a Platform-wide hardening pass (the backup files, the `.preflight-sentinel`, etc., all have the same concern). Worth filing as a follow-up card rather than a one-off.

### LOW-4 — `.preflight-sentinel` leaks on failure

`StartupPreflight.TryWriteSentinel` writes `.preflight-sentinel`, then deletes it. If the write succeeds but the delete fails (e.g., antivirus locks the file mid-test), the sentinel stays in the data directory. Unit test `Validate_DoesNotLeaveSentinelFileBehind` covers the happy path. Not worth hardening.

### LOW-5 — `File.Copy` + `catch (IOException) when (File.Exists(backupPath))` heuristic

`File.Copy(src, dst, overwrite: false)` throws `IOException` when dst exists. The `when (File.Exists(backupPath))` filter is a proxy for "this was a file-exists collision, not a generic I/O failure." It's a reasonable proxy but the canonical check on .NET 10 is `ex.HResult == unchecked((int)0x80070050)` (ERROR_FILE_EXISTS, `HResults.ERROR_FILE_EXISTS` constant). The filter-by-filesystem-check works but reads less obviously. Not a fix — just a note that the intent could be clearer.

### LOW-6 — Pruning uses `CreationTimeUtc`, not `LastWriteTimeUtc` or filename timestamp

Spec §6.5 doesn't mandate an ordering mechanism. `CreationTimeUtc` is correct for "keep newest 5" in normal operation. The caveat: if a backup is copied into `backups/` out-of-band (operator restore workflow, rsync, etc.), `CreationTimeUtc` reflects the copy time, not the original backup time. Alternative: parse the filename timestamp (which is part of the spec'd filename shape). Not a bug; just a robustness choice that's worth flagging for a future hardening pass. Filename-timestamp sort would survive filesystem-attribute rewrites.

### LOW-7 — `MigrationRunner` internal helpers marked `internal` for test access

`BuildBackupPath` and `TryPruneBackups` are `internal static` so tests can call them. That's the right call given Collabhost's `InternalsVisibleTo` posture, and the naming (`BuildBackupPath` vs `public`-level surface) makes the test-only intent clear. No action needed.

---

## Questions (for Remy or Bill)

### Q1 — Should the `COLLABHOST_CADDY_PATH`-style preflight check (§5.2 item 3) land here, or with #156.2?

Spec §5.2 item 3: "Caddy binary resolves or soft-fails cleanly." This references `CaddyResolver` from #153 Phase 2. That code shipped. But #156.1's preflight doesn't run the resolver.

Defensible because the resolver is already run by `ProxyManager` at hosted-service start — re-running it in preflight is duplicative work. The spec framed the preflight check as "capture the result and feed it forward to §9" which would be more relevant when the `ProxyAppSeeder` gate lifts (#156.2). So deferring to #156.2 matches intent.

**No action.** Confirming my read: this was correctly deferred.

### Q2 — Is `effectiveDataDir` resolution at `Program.cs:37-38` robust against relative paths like `./data/collabhost.db`?

```csharp
var (_, resolvedDataDir) = DataRegistration.ResolveConnectionString(builder.Configuration);
var effectiveDataDir = resolvedDataDir ?? Path.Combine(AppContext.BaseDirectory, "data");
```

`DataRegistration.ResolveConnectionString` returns `Path.GetDirectoryName(src)` from the SQLite connection string. For the shipped default (`"Data Source=./data/collabhost.db"`), this returns `"./data"` — a relative path. The preflight then tries to create `./data` relative to CWD, not `AppContext.BaseDirectory`.

In the dev worktree CWD is usually `backend/Collabhost.Api/`, which makes this work (the `.data` ends up under the bin output). In production the install script probably `cd`s to the install dir before launching, so CWD and `AppContext.BaseDirectory` align. But if an operator invokes `collabhost` from a different CWD, preflight creates `./data` in a surprising place.

This isn't a #156.1 regression — the behavior matches pre-#156 code. It's a latent issue in the `./data/collabhost.db` default. Worth noting because §5 preflight now elevates the data directory to "first thing we touch" status; any CWD-sensitivity becomes more visible.

**Recommendation:** file a follow-up card to canonicalize the relative-path default to `AppContext.BaseDirectory`-relative. Not a #156.1 blocker.

### Q3 — Does integration-test migration-per-test add meaningful runtime?

Every `ApiFixture.InitializeAsync` now triggers `MigrateWithBackupAsync` (in addition to the old path where migrations ran if `IsDevelopment()`). Each test creates a fresh temp directory, so it's always "first-run migration, no backup." With 609 Api.Tests, the aggregate cost is nontrivial.

Remy reported all tests pass. Timing not called out. Worth a `dotnet test --logger:console;verbosity=normal` + runtime comparison against `main` if we're anywhere near the CI budget. Not a fix; just a calibration note.

---

## Spec-drift items

**Items you named to call out:**

### SD-1 (YOUR ASK) — §5.2 user-types-dir creation deferred to #156.2

**Spec context:**
- §5.2 item list doesn't explicitly mention user-types-dir creation (says only "Data directory is writable").
- §8.3 says: "the scan directory should be created by `StartupPreflight` (§5) if it doesn't exist."
- §8.5 code seam says: "`Platform/StartupPreflight.cs` -- new; also ensures the user-types directory exists (0700 on POSIX)."

**My read:** Remy's call to defer is **defensible, not a bug.** Creating the user-types directory only has meaning when TypeStore actually reads it — and TypeStore's load path is still dev-gated in #156.1. Creating the dir now would be cosmetic work with no runtime effect; creating it in #156.2 at the same time TypeStore starts reading it is the cleaner split.

**However:** the spec IS internally inconsistent. §5.2 enumerates preflight checks and the user-types dir isn't listed (partial spec). §8.3 and §8.5 both name preflight as the creator (complete spec). An implementer reading §5.2 sees nothing; an implementer reading §8 sees a requirement. Remy picked the lighter-weight reading, which is reasonable.

**Resolution:** fold into the #156.2 spec-reconciliation commit. Either:
- Move the user-types-dir check out of §8.3/§8.5 and into a §5.2 bullet (keeps §5 authoritative for preflight), OR
- Add a §5.2 bullet referencing §8.5 ("§5 also prepares the user-types directory; see §8.3 for rationale and §8.5 for mechanics").

**Recommendation:** the latter. The user-types-dir concern is a TypeStore enablement detail; §8 is its natural home. §5.2 just gets a pointer. I'll own this edit when #156.2 ships.

### SD-2 — §6.2.1 sentinel is read at `Program.cs:115`, not during preflight

Covered in MED-1. Worth reconciling in the #156.2 spec commit.

### SD-3 — §5 preflight doesn't carry forward `BackupsDirectory`; Program.cs rebuilds it

`PreflightResult.BackupsDirectory` IS exposed (and the preflight creates the directory). But `Program.cs:117` does `Path.Combine(effectiveDataDir, StartupPreflight.BackupsSubdirectory)` from scratch instead of reading `preflightResult.BackupsDirectory`. Two places compute the same value; not wrong, just redundant.

Minor. Not worth a fix.

### SD-4 — Spec §12.1 table lists exit codes 30/31/40/50; this PR only emits 10/11/20

Correctly deferred to #156.2 / #156.3 per scope. The tests cover 10/11/20. No drift — scope is honored.

### SD-5 — Spec §7.1 mentions `--migrate` / `--no-migrate` CLI flags deferred; v1 is auto-only

Unchanged by this PR. Confirming scope adherence.

---

## Convention violations

None observed. Code style matches COLLABHOST_KB.md §1:
- Allman braces, Allman parens on multi-line calls.
- Primary constructors with `?? throw` field capture.
- `file sealed` types where appropriate (`FakeCaddyClient` in `ApiFixture`).
- `required` + `init` on `PreflightResult` (actually it's a private-constructor + static-factory pattern; still clean).
- No XML doc comments. Narrative comments only where rationale matters.
- Empty collections as `[]`.
- `ArgumentException.ThrowIfNullOrWhiteSpace` guards at entry.

One tiny style note: `MigrationRunner.cs:27` uses `return [.. pending];` — the spread-into-empty-collection pattern to materialize. Idiomatic and correct. Fine.

---

## Scope notes

**#156.1 correctly scopes to:**
- §4 sequence skeleton (first two inline, rest dev-gated — matches your ask).
- §5 preflight (minus user-types-dir and sentinel-read; see SD-1/SD-2).
- §6 backup + §6.2.1 sentinel.
- §7 migration posture lifted out of the dev gate.
- §12 stderr + exit codes 10/11/20.

**#156.1 correctly does NOT touch:**
- §8 TypeStore gate (still inside `if (app.Environment.IsDevelopment())`).
- §9 ProxyAppSeeder gate (still inside same block).
- §10 first-run detection (implicit; no new derivation code).
- §11 admin-key 3-scenario.
- §14 `Platform:ToolsDirectory` removal from shipped `appsettings.json`.

**Dev-gated block integrity:** the `if (app.Environment.IsDevelopment())` at `Program.cs:181-195` cleanly contains `TypeStore.LoadAsync` + `StartWatching` + `ProxyAppSeeder.SeedAsync` + `MapOpenApi` + `UseCors`. When #156.2 lifts the gate, the first three will hoist out unchanged and the last two stay in a residual `if (IsDevelopment())` for OpenAPI + CORS. **Seam is clean.** Remy's scope fence is exactly where I'd want it.

---

## §4 sequence layering — the load-bearing check

Your question: does `Program.cs` layer the first two phases (Preflight, Migration+Backup) BEFORE the dev-gated block, so #156.2's gate lifts drop cleanly into the sequence without reshape?

**Yes.** The flow is:

1. `StartupPreflight.Validate` — lines 37-62 (before `builder.Build()`).
2. `builder.Build()` — line 110.
3. `MigrationRunner.MigrateWithBackupAsync` — lines 114-179.
4. Dev-gated block: `TypeStore.LoadAsync` + `StartWatching` + `ProxyAppSeeder.SeedAsync` + `MapOpenApi` + `UseCors` — lines 181-195.
5. `BootVersionTracker.Write` registration — lines 198-201.
6. Middleware + endpoints — lines 204-221.
7. `app.RunAsync()` — line 223.

When #156.2 lifts the TypeStore + ProxyAppSeeder gates, those two calls hoist out of the `if (IsDevelopment())` block and sit between the migration try/catch and `BootVersionTracker.Write` registration. Order is preserved. **No reshape needed.** Exactly the seam I designed for.

When #156.3 adds scenario-3 admin-key logic, it goes into `UserSeedService`, which is already a registered hosted service and fires during `app.RunAsync()`'s hosted-services startup. No `Program.cs` reshape. **No reshape needed.**

---

## Test coverage vs §16.1

Spec §16.1 called for:
- `MigrationRunnerTests` — first boot creates DB, normal boot no-ops, pending migrations trigger backup, backup retention rolls at 5, filename-collision exit path, backup filename reflects `{fromSemver}`/`{toSemver}` including `unknown` fallback.
- `StartupPreflightTests` — writable dir → ok, unwritable dir → exit 10, missing user-types dir → created, missing/malformed `.last-boot-version` → `unknown` without halt.
- `BootVersionTrackerTests` — read missing → `unknown`, read valid semver → returned, write is atomic (temp + rename), round-trip after write.

**Coverage delivered:**

`MigrationRunnerTests` (13 tests):
- ✅ First-run creates DB, no backup.
- ✅ No-pending = no-op, no backup.
- ✅ Retention at 5 with 8 planted backups.
- ✅ Retention no-op on 3 planted.
- ✅ Oldest deleted first.
- ✅ Filename includes `from`/`to` semver.
- ✅ `unknown` fallback in filename.
- ✅ UTC timestamp format.
- ✅ `ArgumentException` on empty params.
- ❌ **Missing: filename-collision exit path (exit code 11).** The spec named this explicitly; `MigrationFailedException` is thrown in that branch but no test exercises it. Would require a test that plants a pre-existing file with the exact timestamped name — awkward because the timestamp is "now" — but reachable via a test-seam or a clock abstraction. **Low priority for #156.1** since the failure path is documented and the exception type is exercised elsewhere.
- ❌ **Missing: DB-locked exit path (exit code 11).** `IsLockedException` logic is present but uncovered. Requires either a live `SqliteException` fake or a second `SqliteConnection` holding a lock. Also low priority — the detection is simple and the exit code matches the spec.
- ❌ **Missing: `MigrationFailedException` path for generic migration throw (exit code 20).** Also uncovered. Would require seeding bad schema or forcing EF to fail. Acceptable gap for #156.1.

`StartupPreflightTests` (7 tests):
- ✅ Missing data dir is created.
- ✅ Backups subdirectory is created.
- ✅ Returns resolved data dir on success.
- ✅ Existing writable dir succeeds.
- ✅ No stray sentinel file left behind.
- ✅ Path-points-to-existing-file fails gracefully (the Windows-friendly "plant a file where the dir should be" trick).
- ✅ Empty-string argument throws.

`BootVersionTrackerTests` (9 tests):
- ✅ Missing file returns unknown.
- ✅ Valid semver returned.
- ✅ Leading `v` preserved.
- ✅ Prerelease suffix preserved.
- ✅ Malformed returns unknown.
- ✅ Empty file returns unknown.
- ✅ Write-then-read round-trips.
- ✅ Overwrite replaces.
- ✅ Write persists under expected filename.
- ❌ **Missing: atomic-rename is actually atomic.** The spec called for "atomic rename-on-write so a crash mid-write doesn't produce a corrupt sentinel." The code uses `WriteAllText` + `File.Move(..., overwrite: true)`. Test covers the outcome (round-trip) but not the "no temp file left behind" invariant on failure. Not a blocker — `File.Move` is atomic on POSIX and the test verifies the happy path.

`StartupStderrTests` (3 tests):
- ✅ Summary + details + recovery + exit code all emitted.
- ✅ No details / no recovery path.
- ✅ Recovery steps numbered sequentially.
- Collection-disabled parallelism is correct given `Console.SetError`.

**Overall coverage: 32 new tests (Remy reported 23; the difference is preflight + stderr tests).** The three gaps (filename-collision, DB-locked, generic-migration-throw) are all MigrationRunner failure paths — worth tracking as a follow-up. None block shipping #156.1.

---

## Integration / existing-tests impact

`ApiFixture` is unchanged. Every integration test now:

1. Creates a temp dir.
2. Triggers `StartupPreflight.Validate` (succeeds — dir is writable).
3. Triggers `MigrationRunner.MigrateWithBackupAsync` (first-run, no backup).
4. Triggers `BootVersionTracker.Write` on `ApplicationStarted` (writes `.last-boot-version`).
5. Runs the test.
6. Deletes the temp dir.

Remy reported all 609 Api.Tests + 13 AppHost.Tests pass. Given the path is straightforward first-run migration (which is exercised by `MigrationRunnerTests.MigrateWithBackupAsync_FirstRun_CreatesDbWithNoBackup`), that checks out. No fixture reshape needed.

One harness note: previously, the dev-gate path did `context.Database.MigrateAsync()`; now it's `MigrationRunner.MigrateWithBackupAsync`. Functionally identical for fresh-DB tests, but the new path additionally exercises `GetPendingMigrationsAsync` — which will hit the same test DB. If a test ever runs against a contention-prone DB, the DB-lock detection path would kick in. Not a regression; just a new surface.

---

## Program.cs readability — the diffable-sequence test

Your spec explicitly chose inline-over-hosted-service so "ordering is diffable and we can halt before any hosted service starts." Is the reshape actually readable?

**Yes.** The top-level-statements flow reads as:

```
--version short-circuit
builder setup (config + Aspire)
PREFLIGHT (§5) — halt on fail → exit 10
DI registration
builder.Build()
MIGRATION (§6+§7) — halt on fail → exit N
dev-gated block (#156.2 target)
BootVersionTracker on ApplicationStarted
middleware + endpoints
app.RunAsync()
```

Each phase is visually distinct with a comment header tying to the spec section. The two try/catch blocks for preflight and migration are long but the structure is grep-for-§-reference legible. A reader coming in cold can trace the spec to the code and back in ~30 seconds.

**One readability nit:** the preflight block at 40-63 uses `using (var preflightLoggerFactory = ...)` block scope — fine, but the early-logger factory at line 81-82 is NOT in a `using` block and disposed only at process-end. Inconsistent. `earlyLoggerFactory` is assigned `using var` (which I missed on first read — it IS disposed at block-scope end of the top-level statements, i.e., at process exit). Both are correct; the visual asymmetry just made me second-guess. Not a fix.

**Biggest readability win:** the explicit `return 10;` / `return ex.ExitCode;` top-level returns make the exit-code contract visible at the halt site. An operator-facing failure path isn't hidden in an infrastructure exception handler; it's right there in `Program.cs` where a reader can see which sections halt with which codes.

---

## Summary table

| Severity | Count | Items |
|----------|-------|-------|
| HIGH | 0 | — |
| MED | 2 | MED-1 sentinel-read location drift; MED-2 ApplicationStarted vs "post-Kestrel-listen" precision |
| LOW | 7 | logger-factory dupes; IsLockedException message fallback; POSIX mode bits; sentinel-file cleanup on race; IOException filter style; pruning by CreationTime vs filename; internal test surface |
| Questions | 3 | CaddyResolver in preflight (confirmed deferred); relative `./data` path resolution; per-test migration runtime |
| Spec-drift | 5 | SD-1 user-types-dir (your call); SD-2 sentinel read location; SD-3 redundant BackupsDirectory compute; SD-4/SD-5 scope-honored items |
| Convention violations | 0 | — |

---

## Verdict

**Ship-with-fixes. None of the fixes block #156.1 merging.** The three missing MigrationRunner failure-path tests (filename-collision, DB-locked, generic-throw-exit-20) are worth adding as a follow-up but aren't load-bearing. Spec drift is real but all resolvable in the #156.2 spec-reconciliation doc commit (which I'll own, per my standing offer in TODO.md).

**For #156.2 handoff:**

- Before TypeStore gate lift: decide on SD-1 — does preflight create the user-types dir, or does TypeStore do it at load time? Either way, fix the spec inconsistency.
- When lifting the TypeStore + ProxyAppSeeder gates, the comment at `Program.cs:183` is the marker — that's where the hoisted calls land, between migration try/catch and `BootVersionTracker.Write`.
- Scenario-3 admin-key (#156.3) goes into `UserSeedService`, not `Program.cs`. No reshape of the top-level flow.

Strong positive: this is the cleanest scope fence I've reviewed from Remy. The `// TypeStore + ProxyAppSeeder gates remain in #156.1. Lifted in #156.2.` comment at line 183 is exactly the kind of self-documenting hand-off I'd want to see for a multi-PR feature. He's thinking about the reader who's going to pick this up at the next sub-phase.

---

**Marcus**
`Co-Authored-By: Marcus <marcus@collabot.dev>`
