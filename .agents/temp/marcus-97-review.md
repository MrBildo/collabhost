# Marcus review — PR #97 (#156.3, admin-key 3-scenario)

**Branch:** `feature/156-03-admin-key` @ `de5092a`
**Scope:** +585 / -46, five files (UserSeedService, _Registration, Program.cs, plus two new test files).
**Reviewer:** Marcus (read-only, contract-fidelity pass)

---

## Verdict

**Ship.** One LOW worth addressing in this PR, two LOWs and a MED that can land as a trailing cleanup card. Contract fidelity to production-startup.md §11 is tight. The delta from main cleans up pre-existing sloppiness in the registration layer (credential-in-LogWarning, registration-side key generation). This is the cleanest of the three #156 sub-PRs.

Phase 4b unblocks.

---

## Exit-code ruling

**(a) Keep 40 reused. Do not split.**

Rationale:

- §12.1's OQ-5 ruling explicitly reserves the right to revise codes until operators start depending on them. We're pre-operator-adoption; we have latitude.
- The stderr shape is what actually drives remediation (summary + details + recovery steps + code). Both proxy-seed and user-seed halts share the same recovery shape: "seeder threw during startup → likely DB corruption → restore backup / file issue." The operator runbook is identical.
- Splitting to 41 would suggest the two failures need distinct operator responses. They don't. If we add a third seeder later and each gets its own code, we create a "seeding codes 40-49" sub-taxonomy that we'd have to document and freeze. Not worth it.
- The distinguishing information — *which* seeder threw — is already in the stderr summary line ("proxy app seeding failed" vs "admin user seeding failed") and in the structured LogCritical. Code is for the scripted-remediation case; humans get the richer artifacts.

**No spec update required** for (a). If Remy ever writes a follow-up card to codify "seeding codes are coalesced under 40 by policy," that's a §12.1 note, not a rewrite. Not required for this PR.

**If Bill disagrees and wants (b):** the spec touch is §12.1's table — add row `41 | Admin user seeding threw unexpectedly | Operator (file issue; usually DB corruption)` and update the informational-only-in-v1 sentence. Trivial but not necessary.

---

## Strong positives

1. **Exact spec contract on all three scenarios.**
   - Scenario 1: empty-DB + no configured key → `Ulid.NewUlid()` → insert `Admin` → `Console.WriteLine($"[Collabhost] Admin key: {adminKey}")`. Preserves #152 stdout shape byte-for-byte. Log hint emits the first 8 chars only, matching the pre-existing mask. ✔
   - Scenario 2: empty-DB + configured → insert `Admin` with configured key, info log `"Admin user seeded with configured admin key"`, NO stdout. Explicit `if (wasGenerated)` split makes the intent unmistakable. ✔
   - Scenario 3: existing DB + configured key not matching any `AuthKey` → new admin row, `Name = "Admin (recovery)"`, info log `"Configured admin key is new -- created additional Admin user for recovery"`. Existing admin untouched (test explicitly asserts the existing row's `Name` and `Role` are preserved). ✔

2. **Precedence (§11.2, §11.5) is implemented exactly as specced.** `COLLABHOST_ADMIN_KEY` is read via `Environment.GetEnvironmentVariable` — not relying on the default env-var provider (which doesn't auto-bind to `Auth:AdminKey` under the flat convention). `PostConfigure<AuthorizationSettings>` overlays the bound settings so env wins over config. Mirrors the Phase 3 `COLLABHOST_DATA_PATH` / `COLLABHOST_USER_TYPES_PATH` idiom precisely.

3. **Whitespace handling is correct on both seams.** `_Registration.cs` uses the captured-local + `IsNullOrWhiteSpace` ternary at registration time (line 22-23). `UserSeedService` re-applies the same guard at seed time (line 50-52). Belt-and-suspenders, and the seed-time guard protects against an appsettings.json value that slipped through at some future point (e.g., env unset but config has `"AdminKey": "   "`). Good defense.

4. **Idempotency is covered by explicit tests.** `ConfiguredKeyMatchesExistingUser_NoOp`, `ConfiguredKeyMatchesNonAdminUser_NoOp`, `NoConfiguredKeyDbHasUsers_NoOp`, and `RestartWithSameConfiguredKey_StableSingleAdmin` (three boots, one admin) lock the contract.

5. **Promotion from `IHostedService` → inline phase (8) is correct per §4.** The spec's numbered sequence puts UserSeedService at position (8), after ProxyAppSeeder (7), before TypeStore.StartWatching (9) and the remaining hosted-service startup. Remy's Program.cs placement matches the spec exactly. Timing is unambiguous: seeding runs **before** `app.RunAsync()` (line 354), so before Kestrel accepts any request. The first request against the seeded DB is guaranteed to see the admin row.

6. **Cleanup is a net-positive to the registration layer.**
   - Pre-existing `_Registration.cs` generated a temp ULID and emitted it at `LogWarning` level with the full key in the structured payload. That was a credential-in-structured-logs leak. New code never generates at registration and never logs the key value.
   - `AddCollabhostAuthorization` no longer needs an `ILogger` parameter. The `earlyLoggerFactory`/`earlyLogger` scaffolding in Program.cs is removed. Less surface, fewer seams.

7. **Activity-log tagging is refined.** `seedKind: "first-run"` vs `"recovery-override"` in the `ActivityEvent.MetadataJson` gives the activity log enough signal to tell the two code paths apart. The existing `try/catch` around `RecordAsync` is preserved (Collaboard lesson: activity-log failures must not halt seed).

8. **Test isolation for the process-wide env var is handled correctly.** `AuthorizationRegistrationTests` uses `[Collection] + DisableParallelization` and `try/finally` to restore `COLLABHOST_ADMIN_KEY` to `null`. Five tests cover env-wins, env-unset-falls-back, both-unset-null, whitespace-falls-back, empty-falls-back. Complete coverage of the precedence matrix.

---

## Issues

### HIGH
None.

### MED

**MED-1 — Configured admin-key length is not validated against the schema's `MaxLength(26)`.**
`Data/UserConfiguration.cs:24-25` declares `AuthKey` as `MaxLength(26)` (and `IsUnique`). The schema is sized for ULIDs. This PR formally promotes operator-supplied keys (`COLLABHOST_ADMIN_KEY` / `Auth:AdminKey`) to first-class input — but there is no validation that a configured key conforms to the 26-char constraint. An operator who sets `COLLABHOST_ADMIN_KEY=my-long-passphrase-2026` will hit either silent SQLite truncation or an `EF Core` / provider exception during `SaveChangesAsync`, which then becomes a startup halt with exit 40 and a cryptic "seeder threw" message.

The schema is pre-existing; the "what does a valid admin key look like" contract is implicit ("a ULID"). This PR is a reasonable place to either:

- **(minimal)** Add a pre-insert length check in `SeedFirstRunAsync` / `InsertAdditionalAdminAsync`: `if (configuredKey.Length > 26) throw new InvalidOperationException("COLLABHOST_ADMIN_KEY must be 26 characters (ULID shape).");`. Catch in the outer try in Program.cs — gets the proper stderr block with a recovery step instead of a "seeder threw" generic.
- **(ideal)** Add a dedicated exit code and a custom recovery-steps block for "configured admin key is malformed."

**Not blocking.** Recommend a small follow-up card ("validate operator-supplied admin-key shape") rather than holding this PR. The spec §11.2 does not mandate key-shape validation; it's an implicit contract from the schema. If Bill prefers to address in-PR, the minimal version is ~5 lines.

### LOW

**LOW-1 — Scenario 3 test keys are not valid ULIDs.**
`UserSeedServiceTests` uses constants like `"01EXISTING0ADMIN0KEY000000"` and `"01BREAK0GLASS0KEY0000000AA"`. These are 26 chars (good), but Crockford Base32 excludes `I`, `L`, `O` — and these strings contain `I`, `L`, `O`. They'd fail `Ulid.Parse`. For the `AuthKey` column (opaque string) they work, but a future dev reading the tests may reasonably assume these are parseable ULIDs, misunderstand the constraint, or mis-copy them as real seed data. Suggest either using `dotnet run --file tools/generate-ids.cs` to generate real ULIDs, or renaming to something like `"TEST-KEY-EXISTING-A"` padded to 26 to make the "opaque test string" intent explicit.

**LOW-2 — Activity-log metadata `seedKind` value list is now implicit.**
`"first-run"` and `"recovery-override"` are string literals in two methods. If a third seed kind is ever added, it's easy to miss. A `static class ActivityMetadata.SeedKinds { public const string FirstRun = "first-run"; public const string RecoveryOverride = "recovery-override"; }` (or similar) would make the vocabulary discoverable. Not worth doing in this PR; file as a trailing thought if anyone touches seed metadata again.

**LOW-3 — `AuthorizationSettings.AdminKey` is `{ get; set; }` rather than `{ get; init; }`.**
Pre-existing. The KB style rule is `required` + `init` on settings. However, `PostConfigure` requires a `set`-able property to overlay the env override. Moving to `{ get; init; }` would break the current registration pattern; the alternative is to construct a fresh `AuthorizationSettings` via `Bind` + post-overlay rather than mutating in-place. Not worth churning here, noting only because the pattern appears throughout #156 and is worth standardizing eventually.

---

## Security review notes

Admin key is credential material. Reviewed with paranoia.

1. **No DEBUG-level logging of the key.** Confirmed. Only the first-8-char `keyHint` is logged at Info, and only when `wasGenerated` (Scenario 1) — matching pre-existing behavior. Scenarios 2 and 3 log no key material at all.

2. **No exception-message leak.** The `catch` blocks in Program.cs (phase 8) log the exception type + message via structured fields and render only `$"{ex.GetType().Name}: {ex.Message}"` to the stderr details. Neither scenario 2 nor 3 puts the key into any exception — the only throw surfaces are `SaveChangesAsync` (DB-level) and the activity-log `RecordAsync` (caught internally). If SQLite ever throws a constraint-violation that echoes the offending value, the key could appear in an exception message. Unlikely in practice (SQLite's unique-index violation message is the index name, not the row value) but worth being aware of.

3. **No timing-attack surface in the seeder itself.** `db.Users.AnyAsync(u => u.AuthKey == configuredKey, cancellationToken)` compiles to a SQL `WHERE AuthKey = @p` with the unique index lookup — constant-ish on the DB side, no user-visible timing. Seed runs before Kestrel; no attacker has a timing channel.

4. **Pre-existing timing-attack surface on `AuthKeyResolver`.** Noting only for context: `AuthKeyResolver.ResolveAsync` uses `authKey == adminKey` (string equality, bail-on-first-mismatch) and `UserStore.GetByAuthKeyAsync` uses a SQL `WHERE AuthKey = @p` equality query. Both are theoretical timing-attack surfaces on the request path. This PR does not change that behavior. File as a separate hardening card if we want — `CryptographicOperations.FixedTimeEquals` after base64/hex-decoding, or hash-based lookup. Out of scope for #156.3.

5. **`AuthKeyResolver` config-bypass transient user branch (pre-existing).** `AuthKeyResolver.cs:29-49` creates a transient `User { Name = "Admin (config bypass)" }` if the config admin key matches but no DB row exists. With #156.3, Scenario 2 guarantees a DB row exists at boot, so this branch only fires if:
   - The admin user is deleted at runtime (Remy's `UserEndpoints` delete path, or a manual DB edit), or
   - A request arrives before the seed phase completes — which §4 makes structurally impossible since seed runs before Kestrel.

   So the transient-user branch is effectively a safety net for the delete-while-running case and a dead code path on the boot-order case. Not a bug — appropriate defense — but worth mentioning as part of the security posture: the config admin key is a *persistent* bypass, not just a bootstrap mechanism. `AuthKeyResolver` treats the configured key as permanently privileged. Spec-consistent with the "break-glass" framing, but operators must understand that revoking a compromised `COLLABHOST_ADMIN_KEY` requires both unsetting the env/config AND restarting.

6. **No key value in metrics, traces, or activity-log payloads.** Confirmed. `ActivityEvent.MetadataJson` includes only `{ role, seedKind }`. No key.

7. **Stdout vs structured log split is preserved.** The design intent — operators running interactively see the key in stdout, log aggregators see only `keyHint` — is intact. Operators running under systemd/cron who redirect both streams should still capture stdout for first-boot. INSTALL.md (owned by #158 per §11.4) is where that lives; not a #156.3 concern.

---

## Questions

1. **Exit code 40 reuse:** ruled in (a) above. Remy, you're fine. Ship as-is unless Bill says otherwise.

2. **Integration test for Scenario 3:** Remy flagged this as deliberate. I agree — Scenario 3 is DB-state-dependent, and the fixture's "never mutates admin keys mid-life" invariant is load-bearing for the rest of the integration suite (other tests assume a stable seeded admin). Forcing Scenario 3 into integration would require a fixture variant or a test-specific harness, which is disproportionate for a path that's already covered by six unit tests hitting a real SQLite file. Unit coverage is sufficient here. **Not a gap.**

3. **Configured-key length validation (MED-1):** preference — in-PR 5-line length check, or follow-up card? My lean is follow-up card; the current behavior fails closed (halt with exit 40 + stderr), it's just a less-friendly message than an operator-recoverable one.

---

## Convention violations

None material. All primary-constructor + `_field ?? throw` + Allman braces + file-scoped namespaces are clean. `dotnet format` will accept this as-is.

Minor style notes (not blocking):

- `UserSeedService.cs:105` — `adminKey[..Math.Min(8, adminKey.Length)] + "..."` preserves pre-existing behavior. Fine.
- `_Registration.cs:22` — reading `Environment.GetEnvironmentVariable` inside a DI registration method is the §11.5-prescribed idiom. Reviewed this pattern in #156.1 and #156.2; consistent application.
- `AuthorizationSettings` remains `{ get; set; }` (LOW-3). Pre-existing. Not introduced by this PR.

---

## Scope notes

Confirmed scope fences hold:

- **No CLI flag changes.** `--admin-key` is deferred per §11.2; Program.cs `args.Any(a => a is "--version" or "-v")` remains the only flag.
- **No admin-key recovery mechanism changes.** §11.4 defers to #158. Nothing in this PR touches key-recovery UX.
- **No INSTALL.md changes.** Owned by #153 Phase 4b.
- **No other #156 surface changes.** Did not touch §5 preflight, §6 backup, §7 migration, §8 TypeStore, or §9 ProxyAppSeeder. Did not touch `AuthKeyResolver`, `UserStore`, or `AuthorizationMiddleware` (verified via `git diff` on those paths — no changes).
- **No schema changes.** The `User` entity, `UserConfiguration`, and the `InitialCreate` migration are untouched.

Phase 4b dependency accounting: §11 was the last §-level item #156 owed against #153 Phase 4b. After this merges, #156's contract is complete and Phase 4b is unblocked per the gating diagram in spec §2 and §18.

---

**Recommendation: ship.** One follow-up card for MED-1 (configured-key length validation). LOW-1 test-string cleanup can roll into the same card or stay as-is — caller's choice.

— Marcus
