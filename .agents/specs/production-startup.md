# Production Startup -- Spec

**Card:** #156 -- Production startup: seeding, migrations, and data-safety posture
**Author:** Marcus (design lead)
**Implementer:** Remy
**Status:** R1 -- initial design proposal. Awaiting Dana + Remy review and Bill sign-off on open questions.
**Gates:** #153 **Phase 4b** (installer + INSTALL.md). Phase 4a of #153 does not depend on this spec.
**Related cards:** #152 (admin key stdout / first-run, superseded by §11 here), #158 (admin-key UX surface -- complementary, not blocking), #159 (settings resolution -- `#156` owns the `Platform:ToolsDirectory` removal called out there), #161 (smart-merge of shipped `appsettings.json` defaults -- parking), #162 (log directory / pre-crash diagnostics), #153 (release pipeline -- consumer)
**Date:** 2026-04-20

---

## 1. Overview

This spec defines Collabhost's production startup contract: what runs, in what order, under what safety rails, when the API boots in a non-Development environment. It exists because today `Program.cs` gates three production-critical operations -- EF Core migrations, `TypeStore.LoadAsync` + `StartWatching`, and `ProxyAppSeeder.SeedAsync` -- behind `app.Environment.IsDevelopment()`. If #153 Phase 4b ships as drafted (lift the gate, ship the binary), operators would get:

- A binary that, on first launch, would successfully migrate and seed -- but with **no backup discipline**, so a future failed migration could corrupt or lose the operator's SQLite database.
- Subsystems (`ProcessSupervisor`, `ProxyManager`) that inject and call `TypeStore` at runtime against an **empty snapshot** because the load call lives inside the dev gate.
- No proxy app seeded, so Tier 1 of the capability system is silently dead on a production deployment.
- No principled story for admin-key generation on upgrade vs first run.
- No designed exit path for the failure modes (DB locked, migration failed, filesystem perms denied, corrupt built-in type JSON, etc.).

This spec is the design that #153 Phase 4b depends on. It is intentionally conservative: migrate-on-startup *with a mandatory pre-migration backup*, fail-fast on any seeding/load step that can't be done idempotently, and put the failure messages somewhere an operator will actually see them.

The spec does not write code. It defines contracts, orderings, invariants, and failure semantics. Code seams are referenced by path; method signatures appear only where they clarify intent. Remy implements.

### 1.1 What this spec does NOT cover

- **Health-check execution.** The capability schema exists; runtime logic is a separate deferred feature (v2 architecture doc).
- **SSE deployments / hot-reload / app updates.** Deferred from v2.
- **Structured log directory for pre-crash diagnostics.** Card #162.
- **Smart-merge of shipped `appsettings.json` defaults into operator's file.** Card #161.
- **Admin-key UX surface in the operator's first-boot console.** Card #158. This spec defines the behavioral model; the UX presentation layer is #158's.
- **Windows service / systemd unit integration.** Not in v1.

---

## 2. Locked Inputs (reference)

These come from #153 R2.1, #159, and prior roundtables. #156 designs around them.

| # | Input | Source |
|---|-------|--------|
| 1 | Production deployments use a **single `appsettings.json`**. No `.Local`, no `.Production`. | #153 §2.5, #159 locked |
| 2 | `appsettings.json` is the **operator's persistent configuration file** and is **preserved on reinstall**. Every new shipped key must have a working in-code default. | #153 §2.5, §9.7; #161 parking |
| 3 | Default SQLite path changes `./db/collabhost.db` → `./data/collabhost.db` relative to the binary's `AppContext.BaseDirectory`. | #153 §12.2 |
| 4 | `COLLABHOST_DATA_PATH` env var overrides the data directory (connection string derived). | #153 §12.3 |
| 5 | Install layout is `$HOME/.collabhost/bin/{collabhost,caddy,appsettings.json,INSTALL.md,LICENSES/}` with `data/` created next to the binary at first run. | #153 §12.2 |
| 6 | `ProxyManager` exposes a volatile `proxyState` (`starting \| running \| failed \| disabled \| stopped`) surfaced in `/api/v1/status`. | #153 §6.4.2 |
| 7 | `ASPNETCORE_ENVIRONMENT` is the standard .NET env var; production runs default to `Production` (unset/absent defaults to `Production` per ASP.NET Core). | ASP.NET Core convention |

---

## 3. Problem Surface -- today's `Program.cs`

Current state (abbreviated; see `backend/Collabhost.Api/Program.cs` lines 70-88):

```csharp
if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    await using var context = await db.CreateDbContextAsync();
    await context.Database.MigrateAsync();

    var typeStore = app.Services.GetRequiredService<TypeStore>();
    await typeStore.LoadAsync(CancellationToken.None);
    typeStore.StartWatching();

    var proxySeeder = scope.ServiceProvider.GetRequiredService<ProxyAppSeeder>();
    await proxySeeder.SeedAsync(CancellationToken.None);

    app.MapOpenApi();
    app.UseCors(p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
}
```

Five operations, one gate. Three of them (`MigrateAsync`, `LoadAsync` + `StartWatching`, `SeedAsync`) are production-critical. Two (`MapOpenApi`, `UseCors`) are correctly dev-only.

**F1 -- the production-critical ones must move out of the gate.** `ProcessSupervisor` and `ProxyManager` inject `TypeStore` and call `.GetBySlug(...)` / `.HasBinding(...)` at runtime. In non-Development those lookups run against the empty snapshot the field initializer seeds, so:

- `ProcessSupervisor` can't resolve an app's capability bindings.
- `ProxyManager` can't find the proxy app to manage.
- No user types load. No built-in types load.

This is the `F1` finding from session 15. It is not "needs polish for production" -- it is **the production code path is broken today** and has been hidden by the fact that we've only ever run under Development.

The migration call is a subtler trap. Today it runs once on a developer machine against a throwaway DB. Moved as-is to production, it runs on an operator's live DB with no backup, no rollback, and no failure signal other than a startup exception. The Collaboard pattern -- and the industry norm -- is to back up before applying.

---

## 4. Startup Sequence (production) -- the contract

This section defines the end-to-end production boot sequence. Each numbered phase has a defined success criterion, a defined failure mode, and a defined exit code / log signal.

```
(1) Configuration resolve  -- §2.5 / #159
(2) Environment preflight  -- §5
(3) First-run detection    -- §10
(4) Pre-migration backup   -- §6
(5) Schema migration       -- §7
(6) TypeStore load         -- §8
(7) ProxyAppSeeder         -- §9
(8) UserSeedService        -- §11 (admin-key 3-scenario)
(9) TypeStore StartWatching  -- §8.3
(10) HostedService startup (Supervisor, ProxyManager, etc.)
(11) Kestrel listens
```

Phases 1-9 run **synchronously before `app.RunAsync()` returns to the message loop**. Any failure in phases 2-8 halts startup with a non-zero exit code and a clear stderr message (§12). Phases 9-11 are the normal ASP.NET Core hosted-service + Kestrel lifecycle -- failures there are handled by ASP.NET Core's existing mechanics.

This ordering is load-bearing. In particular:

- **(4) must precede (5)** -- backups are only useful if taken before the mutation.
- **(6) must precede (7)** -- `ProxyAppSeeder` reads `TypeStore.GetBySlug("system-service")`.
- **(6) must precede (10)** -- `ProcessSupervisor` and `ProxyManager` are hosted services that call `TypeStore` in their `StartAsync` paths.
- **(9) must follow (6) + (7)** -- the file watcher should only fire against a loaded snapshot, and only in Production if we decide it stays (§8.3 open question OQ-4).

---

## 5. Environment Preflight

Before anything touches disk or DB, validate the resolved environment.

### 5.1 Decision -- run a preflight

Yes. Small, cheap, high-signal. The failure mode for "skip preflight" is cryptic downstream exceptions (`UnauthorizedAccessException` mid-migration, `FileNotFoundException` on the embedded resource, etc.) that an operator has to trace backward.

### 5.2 Checks

1. **Data directory is writable.** Resolve the effective data directory (per §7.2), create it if missing (0700 on POSIX), try to write + delete a sentinel file. Fail with clear message + exit code 10 (§12.1) if not writable.
2. **`AppContext.BaseDirectory` is readable.** We need the embedded-resource assembly and the bundled Caddy sidecar both accessible. Any read failure is a packaging bug, not a runtime condition.
3. **Caddy binary resolves or soft-fails cleanly.** `CaddyResolver` runs as Phase 2 of #153. Its result is captured and fed forward to §9. If it fails, the proxy subsystem enters `disabled` at boot (§9) -- not a startup halt.

**What we deliberately do NOT check at preflight:**

- DB file presence. First-run detection is its own phase (§10).
- Port availability. That's `ProxyManager`'s and `Kestrel`'s concern at their own startup.
- Caddy API reachability. Post-launch probe (#153 §6.4.2).

### 5.3 Code seam

New class `Platform/StartupPreflight.cs` with a single `ValidateAsync(IHostEnvironment, IConfiguration, ILogger) -> PreflightResult` method. Called from `Program.cs` after configuration is built and before the scope is created.

---

## 6. Pre-Migration Backup

### 6.1 Decision -- mandatory before every `MigrateAsync` call in Production

Every production boot that *has pending migrations* takes a backup first. No exceptions. Backups are cheap (SQLite file copy, no schema lock yet), recoveries are priceless, and the absence of this discipline on Collaboard has been cited repeatedly as the reason we can't casually upgrade schema.

The hook point is **"has the EF Core schema changed?"**, detected via `context.Database.GetPendingMigrations()`. If the count is 0, no migration runs and no backup is taken -- a normal-boot reboot is fast and touches nothing. If the count is > 0, the backup happens synchronously before `MigrateAsync()`.

### 6.2 Backup filename format

```
collabhost.db.bak-{yyyyMMddTHHmmssZ}-pre-{fromVersion}-to-{toVersion}
```

Example: `collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0`

- `{yyyyMMddTHHmmssZ}` -- UTC timestamp (sortable, filesystem-safe).
- `{fromVersion}` -- the last applied migration name, truncated to the version prefix if available, else the literal `unknown`.
- `{toVersion}` -- the last pending migration name (the target), or the semver tag from `VersionInfo.Current` if we decide to prefer that (OQ-1).

The filename is long by design. When an operator lands in `data/` looking for "which backup matches which upgrade," the timestamp + version band is worth the extra characters.

### 6.3 Backup location

`{dataDirectory}/backups/` -- a new sibling of `collabhost.db`. Created 0700. On POSIX, owned by the Collabhost process user.

**Rationale for a subdirectory, not the DB dir itself:** backups accumulate. Keeping them out of the root of `data/` makes `ls data/` meaningful.

### 6.4 Backup mechanism

`File.Copy(sourceDb, destPath, overwrite: false)` with `overwrite: false` is intentional -- a filename collision means the same boot ran twice in the same second, which is impossible in normal operation. If it happens, halt with exit code 11 (§12.1) and log "backup filename collision, refusing to proceed."

SQLite's [Backup API](https://www.sqlite.org/backup.html) is the bulletproof option (takes a read lock, handles WAL, works on a live DB). For Collabhost's case -- pre-migration, no other connections yet established -- a plain file copy is sufficient and simpler. If we ever take backups *during* normal operation (e.g., for a `/api/v1/admin/backup` endpoint), we switch to the Backup API. Not in v1.

### 6.5 Retention policy

**Keep the last 5 pre-migration backups**, rolled by creation time. Older files are removed as the 6th is created.

- 5 is a number, not a law. It balances "recover from a bad upgrade" against "infinite accumulation in `data/backups/`."
- An operator who wants infinite retention can symlink `data/backups/` to any location they like.
- An operator who wants zero retention can delete the directory. The code re-creates it.

### 6.6 Recovery procedure (INSTALL.md content)

`INSTALL.md` gets a section under Troubleshooting:

> **If an upgrade fails and Collabhost won't start:**
>
> 1. Stop Collabhost (it's probably already stopped; just confirm no process is running).
> 2. `cd` to your data directory (`$HOME/.collabhost/bin/data/` by default, or wherever `COLLABHOST_DATA_PATH` pointed).
> 3. `ls backups/` to see your pre-migration backups.
> 4. Copy the most recent backup over the live DB: `cp backups/collabhost.db.bak-{...} collabhost.db` on POSIX, `Copy-Item` on Windows.
> 5. Re-install the previous version from [releases](https://github.com/MrBildo/collabhost/releases) -- not the new one that failed to migrate.
> 6. Start Collabhost. You are back to your pre-upgrade state.
> 7. Report the migration failure as an issue. Include the exact version you upgraded from and to, and the stderr text Collabhost printed on the failed boot.

This section is content #156 owns but the actual INSTALL.md file is written as part of #153 Phase 4b. Remy lifts this text when he ships the installer.

### 6.7 Code seam

New class `Data/MigrationRunner.cs` with two methods:

```csharp
public sealed class MigrationRunner
{
    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct);
    public Task<MigrationOutcome> MigrateWithBackupAsync(CancellationToken ct);
}

public sealed record MigrationOutcome(
    bool Migrated,
    string? BackupPath,
    IReadOnlyList<string> AppliedMigrations);
```

The `MigrationRunner` encapsulates the §6 + §7 concerns together. `Program.cs` (or a hosted service, see §7.4) calls it as a single step.

---

## 7. Migration Posture

### 7.1 Decision -- auto-migrate on startup, with safety rails

**Recommendation: auto-migrate.** Reasoning:

- Operator cognitive load is low. Install, run, done. No "now run `collabhost migrate`" step.
- The #153 Phase 4b installer is already a "copy files, run binary" experience. Forcing a manual migration step breaks that model.
- Safety rails (§6 backup, §7.3 failure handling) make the auto path acceptable.
- The alternative -- explicit `collabhost migrate` CLI command -- adds surface without solving a real problem. If an operator is sophisticated enough to run `collabhost migrate` from a script, they're sophisticated enough to put `cp collabhost.db collabhost.db.bak` in the same script.

**Rejected alternatives:**
- **Prompt on upgrade:** no TTY in production. Rejected.
- **Require opt-in env var (`COLLABHOST_AUTO_MIGRATE=1`):** yet another thing operators have to discover. If we ever make opt-in the default, it's because we've lost confidence in our own migration discipline and that's a bigger problem.

**Open question:** OQ-2 below considers whether `--migrate` / `--no-migrate` CLI flags belong in v1.

### 7.2 Which DB do we migrate?

The one resolved by the same precedence chain #153 §12 locks for the data path:

1. `COLLABHOST_DATA_PATH` env var (overrides directory; connection string derived as `Data Source={dir}/collabhost.db`).
2. `ConnectionStrings:Host` in `appsettings.json` (literal connection string).
3. `Data Source=./data/collabhost.db` hardcoded default.

This resolution happens in `Data/_Registration.cs` (Phase 3 of #153 wires the env var; the current code path is the connection string + hardcoded default). The `MigrationRunner` works off `IDbContextFactory<AppDbContext>` and inherits whatever the registration resolved.

### 7.3 Migration failure handling

**Halt startup. Do not boot.** Recovery is the operator's job (§6.6).

Concrete sequence on `MigrateAsync` throwing:

1. Log `LogCritical` with the full exception and the path to the backup just taken.
2. `Console.Error.WriteLine` a human-legible message to stderr with the recovery steps abbreviated (INSTALL.md has the full version).
3. Exit code 20 (§12.1).

**Do NOT attempt to roll back the DB automatically.** SQLite does not give us the transactional guarantee across multiple `ALTER TABLE` statements that would make auto-rollback safe. The backup from §6 is the rollback mechanism, and it is the operator's choice to apply it. Attempting to auto-restore in-process risks corrupting an already-half-migrated file.

**Partial migration is possible.** EF Core migrations are not wrapped in a single transaction by default (SQLite supports DDL-in-transaction but EF Core doesn't always produce coherent transactional batches for multi-statement migrations). If migration N of 3 succeeds and migration N+1 fails, the DB is in a state that is neither "pre-upgrade" nor "fully upgraded." The operator's only clean path is to restore the backup. This is why §6 is mandatory, not optional.

### 7.4 Code seam -- hosted service vs inline in `Program.cs`

Two options:

- **(A) Inline in `Program.cs`:** call `MigrationRunner.MigrateWithBackupAsync(...)` after `builder.Build()` and before `app.RunAsync()`. Simplest, mirrors today's dev-gated pattern.
- **(B) Dedicated `IHostedService` (`MigrationHostedService`):** registered with `AddHostedService`, runs first in the startup order. ASP.NET Core-idiomatic, integrates with OTel hosted-service traces.

**Recommendation: (A) inline in `Program.cs`.** Reasons:

- Ordering with `UserSeedService` (which is already an `IHostedService`) matters, and `IHostedService.StartAsync` ordering is by registration order -- correct but implicit. Inline code makes the sequence visible and diffable.
- If migration fails, we want to halt before *any* hosted service starts. An inline call guarantees that. A hosted service puts us in the ASP.NET Core lifecycle, where stopping early is possible but less ergonomic.
- The existing dev-gated pattern is already inline. Lifting the gate is a smaller delta than restructuring into a hosted service.

**Rejected (B)** for the reasons above. Revisit if we ever have multiple migration-like startup steps that want coherent observability.

---

## 8. TypeStore in Production

### 8.1 Decision -- `LoadAsync` is required in production; runs as part of the startup sequence

No ambiguity: consumers (`ProcessSupervisor`, `ProxyManager`, capability resolver) call `TypeStore.GetBySlug` / `.HasBinding` at runtime. Without `LoadAsync`, those calls return empty. The system does not function.

`LoadAsync` runs **unconditionally in all environments**, right after migrations succeed, in the same scope block. The environment check is dropped.

### 8.2 `LoadAsync` failure handling

`TypeStore.LoadAsync` throws `TypeStoreValidationException` on malformed built-in or user type JSON. For built-in types, that is a **packaging bug** -- the embedded resource shipped broken, and no operator can recover from that without a new binary. For user types, it is an **operator config error** -- they dropped bad JSON in the scan directory.

**Decision -- fail-fast, with the failure mode clearly indicated:**

- Built-in validation failure: log `LogCritical`, print stderr "built-in type validation failed -- this is a packaging bug, please file an issue with the exact stderr output", exit code 30.
- User-type validation failure: log `LogCritical` with the specific file + field errors, print stderr "invalid user type JSON at `{path}` -- fix or remove the file and restart", exit code 31.

The reason for separate exit codes is so that bundled smoke tests (#153 §15.4) can distinguish a "our package broke" failure from an "operator configured wrong" failure.

**Rejected alternative -- soft-fail on user types (drop bad files, keep booting):** this makes the failure invisible. An operator would launch, see the dashboard, not realize their custom `nodejs-worker.json` type never loaded, and waste an afternoon. Halting with a clear message is friendlier than appearing to work.

### 8.3 `StartWatching` -- remains, but with a boot vs operation distinction

Today's `StartWatching` runs on startup, sets up a `FileSystemWatcher` on the user-types directory, and triggers `ReloadAsync` on change. `ReloadAsync` already has robust error handling (invalid JSON = keep current snapshot + warn, valid changes = swap atomically). That code is production-grade.

**Decision -- keep `StartWatching` enabled in production.** It is the only way an operator can add a user type without restarting. The cost (one FSW + a channel + a background processor) is negligible.

**But:** the scan directory should be created by `StartupPreflight` (§5) if it doesn't exist, with the informational log "user types directory initialized, drop `*.json` files here." Today `StartWatching` silently skips if the directory doesn't exist; for production that silence is wrong -- it means "this feature is disabled" without the operator ever knowing the feature existed.

**Open question OQ-4 below:** should we also support reload-on-SIGHUP for environments where FSW is unreliable (some network filesystems, some container overlay mounts)?

### 8.4 `COLLABHOST_USER_TYPES_PATH` resolution

Already covered by #153 §12.3. Precedence `env > config > default (UserTypes relative to AppContext.BaseDirectory)`. `StartupPreflight` resolves the effective path and logs it on every boot. Eases operator troubleshooting.

### 8.5 Code seam

- `Data/AppTypes/TypeStore.cs` -- no contract change. Already throws on validation failure.
- `Program.cs` -- remove the `IsDevelopment()` gate around the `LoadAsync` + `StartWatching` calls.
- `Platform/StartupPreflight.cs` -- new; also ensures the user-types directory exists (0700 on POSIX).

---

## 9. ProxyAppSeeder in Production

### 9.1 Decision -- seeding is idempotent today; enable in production

`ProxyAppSeeder.SeedAsync` already checks `_appStore.GetBySlugAsync("proxy")` and short-circuits if the proxy app exists. Run-it-every-boot is safe and correct. Its current fail modes on the unhappy path are:

- Caddy binary not found: logs warning, returns. Doesn't throw. In the post-#153 world, `CaddyResolver` and the `proxyState` machine own this (we should not re-implement the resolution here).
- `system-service` type not found in `TypeStore`: logs warning, returns. With §8.1 guaranteeing `TypeStore` is loaded before this step, this path is unreachable in a correctly-packaged production build.

### 9.2 Integration with the locked Caddy resolver (§6.4 of #153)

`ProxyAppSeeder` currently calls its own static `ResolveBinaryPath`. After #153 Phase 2 lands, that responsibility moves to `CaddyResolver`. `ProxyAppSeeder` consumes the resolver's result.

If the resolver yields nothing (no env var, no `appsettings.json` value, no bundled sidecar, probe optional), the proxy app is **still seeded** but its process-capability override stores the *intended* binary path as returned by resolution (which may be empty or unreachable). The outcome:

- `ProxyManager` attempts to start the proxy process.
- Start fails (file not found / permission denied).
- `proxyState` transitions to `failed` (or `disabled` if we know up-front the path is missing).
- `/api/v1/status` surfaces the state.
- Operator sees "proxy disabled -- set `COLLABHOST_CADDY_PATH` or place the bundled binary next to collabhost" in the dashboard.

**Rejected alternative -- refuse to seed if Caddy is missing:** creates a bootstrapping hole. An operator who installs Collabhost without Caddy, fixes Caddy, and restarts would get into the seeded state on the second boot only if seed-on-every-boot is idempotent. Which it is. So seed-always is simpler and equivalent.

### 9.3 First-run vs existing-install distinction

No special case. `SeedAsync` is idempotent. A first boot seeds. A subsequent boot no-ops. No migration-like "was proxy seeded in a prior version" concern because the check is slug-based, not version-based.

### 9.4 Seeder failure handling

If seeding throws for an unexpected reason (DB write failure, transaction rollback, etc.), **halt startup**. The proxy subsystem is a core feature; its absence is not a soft degradation. Exit code 40.

### 9.5 Code seam

- `Proxy/ProxyAppSeeder.cs` -- no contract change. Remove its static `ResolveBinaryPath` after #153 Phase 2 wires `CaddyResolver` through (Remy's Phase 2 work).
- `Program.cs` -- remove the `IsDevelopment()` gate around the `SeedAsync` call.

---

## 10. First-Run Detection

### 10.1 Decision -- "first run" is the empty-DB state

Collabhost does not need a filesystem marker or a config flag to know this is its first boot. The DB itself is authoritative:

- **First run** = DB file missing OR DB exists but has zero applied migrations AND zero rows in `Users`.
- **Upgrade** = DB exists, has applied migrations, has pending migrations after binary replacement.
- **Regular boot** = DB exists, no pending migrations.

This maps cleanly to the startup phases:

| Phase | First run | Upgrade | Regular boot |
|-------|-----------|---------|--------------|
| Preflight | Creates `data/`, `data/backups/`, user-types dir | Validates writable | Validates writable |
| Migration runner | Creates DB, applies all migrations, no backup (no prior state) | Takes backup, applies pending | No-op |
| TypeStore LoadAsync | Loads built-ins + user types | Loads built-ins + user types | Loads built-ins + user types |
| ProxyAppSeeder | Seeds proxy app | No-op (already seeded) | No-op |
| UserSeedService | Generates / honors configured admin key (§11) | No-op if users exist; §11 scenario 3 override | No-op |

### 10.2 Why no `FirstRun: true` config flag

Config flags that encode runtime state invite desync. An operator deletes their DB to start over, forgets to flip a flag, and now the boot behavior is wrong. The DB being empty *is* the signal; derive, don't persist. (Matches Bill's stated preference from the process-management roundtable: if data can be derived from current state, don't persist it.)

### 10.3 Code seam

No new code. The three downstream phases (migration runner, proxy seeder, user seeder) each do their own idempotency check. A caller doesn't ask "is this first run" -- the callees are idempotent by construction.

---

## 11. Admin-Key 3-Scenario Behavioral Model

This supersedes card **#152**. The UX surface (exact console output format, recovery if missed) is #158's domain; the *behavior* is specified here.

### 11.1 The three scenarios

**Scenario 1 -- Blind first run.** No admin key configured (neither `Auth:AdminKey` in `appsettings.json` nor `COLLABHOST_ADMIN_KEY` env var nor `--admin-key` CLI arg). DB is empty.

- `UserSeedService` generates a ULID, inserts an `Admin` user with `Role = Administrator` and that key, writes the key to stdout in a distinctive format (#158's call exactly how).
- INSTALL.md's "Your admin key" section documents "on first launch, watch stdout for the line starting with `[Collabhost] Admin key:`" and the recovery path if the operator missed it (§11.4).

**Scenario 2 -- Configured first run.** Admin key provided via one of the configured sources. DB is empty.

- `UserSeedService` resolves the effective key via the §2.5 precedence chain (`CLI > env > appsettings.json`) and inserts the `Admin` user with that key.
- No stdout emission of the key (the operator already has it).
- A short info-level log line confirms: `"Admin user seeded with configured admin key"` (no key value in logs).

**Scenario 3 -- Override on subsequent boot.** DB has users. An admin key is provided via config/env/CLI and **does not match** any existing user's `AuthKey`.

- `UserSeedService` inserts a **new** admin user with that key (not a replacement; the existing admin remains). Rationale: operators lock themselves out of the dashboard. The configured-override path is their break-glass recovery. We don't revoke existing admins in the process because we don't know which of the existing admins is the "real" one.
- Info log: `"Configured admin key is new -- created additional Admin user for recovery"`.
- If the configured key **matches** an existing user, no-op.

### 11.2 Precedence of the configured key source

Per #153 §2.5: `CLI --admin-key > COLLABHOST_ADMIN_KEY env var > Auth:AdminKey in appsettings.json`.

**`--admin-key` CLI flag -- introduced here.** Today only `Auth:AdminKey` exists. Env var and CLI flag are both new. The CLI flag is useful for automation; the env var is useful for the per-invocation startup-wrapper pattern #153 §12 established.

**Open question OQ-3:** do we ship all three sources in v1, or just the env var + config? Scoping question for Bill.

### 11.3 Idempotency and restart semantics

- Repeated boots with the same configured key: scenario 3 matches existing user → no-op. Clean.
- Repeated boots with no configured key and a populated DB: scenario 3 doesn't trigger (no key to match). Nothing changes.
- Repeated boots with no configured key and an empty DB: scenario 1 every time. Since the DB is empty every time, you'd only hit this if something wiped the DB between boots.

### 11.4 Key recovery if the operator missed it (Scenario 1)

Decided surface is #158's; the behavioral options are:

- **(a)** Log to a file (`data/admin-key.txt`, 0600) in addition to stdout, with "delete this file after you've captured the key" instructions. Simple, reliable, but leaves a credential on disk.
- **(b)** Re-emit the key via a `collabhost --show-admin-key` CLI flag that reads the first admin user's key from the DB. Requires DB lookup, is explicit, leaves no on-disk credential.
- **(c)** Require a DB reset ("delete `collabhost.db` and reboot"). Safe but unhelpful.

**My recommendation: (b).** But this is #158's call.

### 11.5 Code seam

- `Authorization/UserSeedService.cs` -- extend current behavior:
  - Add scenario 3 handling (compare configured key to existing users, insert if new).
  - Remove stdout emission when key was configured (scenario 2).
  - No change to the existing scenario-1 stdout behavior.
- `Authorization/_Registration.cs` -- today generates a temp key if `AdminKey` is null (lines 20-38). Move that generation *into* `UserSeedService` so the generation + insertion + logging live together. The current split (generate in registration, consume in seeder) is a holdover from before the seeder existed.
- `Program.cs` -- if `--admin-key` CLI flag lands, parse in the short-circuit block near `--version`.

---

## 12. Startup Failure Modes

### 12.1 Exit code table

| Code | Meaning | Actionable by |
|------|---------|---------------|
| 0 | Normal shutdown | -- |
| 10 | Data directory not writable | Operator (filesystem perms) |
| 11 | Backup filename collision | Operator (unlikely; indicates clock or retry loop) |
| 20 | Migration failed | Operator (restore from backup, file issue) |
| 30 | Built-in type validation failure | Collabhost team (packaging bug) |
| 31 | User-type validation failure | Operator (fix or remove the bad JSON) |
| 40 | Proxy seeding threw unexpectedly | Operator (file issue; usually DB corruption) |
| 50 | TypeStore built-in resource missing | Collabhost team (packaging bug) |

Exit codes above 1 are Collabhost-specific; 0 and 1 are standard (`1` is ASP.NET Core's generic "unhandled exception during host build").

### 12.2 Stderr message shape

Every halt exits via a single place (`Platform/StartupFailure.cs` or similar), which writes to `Console.Error` a block like:

```
Collabhost startup failed: migration failed

Details:
  - Backup created at: /home/collab/.collabhost/bin/data/backups/collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0
  - Migration attempted: AddActivityEvents
  - Exception: System.Data.SqliteException: SQLITE_ERROR: no such column: Foo

Recovery:
  1. See INSTALL.md -> Troubleshooting -> "If an upgrade fails."
  2. Restore the backup listed above.
  3. Re-install the previous version.

Exit code: 20
```

The stderr path is canonical for CI-like / systemd-like contexts where stdout may be captured separately. Operators running interactively see both; log scrapers see the structured log.

### 12.3 Logger vs Console distinction

- **Logger (ILogger):** every failure gets a `LogCritical` with structured fields (migration name, exception, exit code). Captured by whatever logging infrastructure is configured.
- **Console.Error:** the human-readable block above. Redundant with the logger for an interactive operator; necessary for the systemd/cron/script-redirect case where the operator won't read structured logs.

This parallels `UserSeedService`'s existing "log a hint, Console.WriteLine the full key" split.

### 12.4 DB-locked case (concurrent boot / leftover process)

If `MigrateAsync` throws `SqliteException: database is locked`:

- Exit code 20 (migration failed).
- Stderr message: "Another Collabhost process may be using the database. Stop it first. `collabhost.db` is at `{path}`."
- Do NOT retry with backoff. If this is a legitimate concurrent boot (two installs racing), retry loops compound the problem. If it's a leftover process, the operator needs to clean it up, not have us wait.

---

## 13. Upgrade Story

### 13.1 The contract

An operator replaces the `collabhost` and `caddy` binaries (via install-script re-run, per #153 Phase 4b). The operator's `appsettings.json`, `data/` directory, and any user-type JSONs are preserved. The operator starts the new binary.

Expected behavior:

1. Preflight passes (writable data dir, embedded resources present).
2. Migration runner detects pending migrations, takes a pre-migration backup, applies them. On success: one info log line per applied migration + the backup path. On failure: §7.3 halt.
3. TypeStore loads. If the new binary's embedded resources conflict with an operator's user-type JSON (e.g., the new version adds a `python-app` built-in and the operator had a user type with the same slug), the validator rejects the user type per its existing logic.
4. Proxy seeder no-ops (proxy app exists).
5. User seeder: scenario 3 if the operator added a configured key to the env var since the last boot, no-op otherwise.
6. HostedServices start, Kestrel listens, dashboard loads.

No manual steps. No `collabhost migrate` command. No `--force` flag.

### 13.2 What happens if the operator skips a version

We ship v0.1.0 with migrations `[Initial]`. We ship v0.2.0 with migrations `[Initial, AddActivityEvents]`. Operator on v0.1.0 installs v0.2.0 directly: migrations runner sees `AddActivityEvents` as pending, backs up, applies. Clean.

Operator on v0.1.0 installs v0.5.0 with migrations `[Initial, AddActivityEvents, AddFoo, AddBar, AddBaz]`: pending migrations are the last four; backup + apply in order. EF Core handles this.

**Migration squashing (never collapse migrations post-release)** is a Collabhost-team discipline question, not operator-facing. Out of this spec's scope.

### 13.3 What happens if the operator downgrades

`collabhost.db` was touched by migrations `[Initial, AddFoo]`. Operator re-installs the prior binary that only knows about `[Initial]`.

- EF Core's `MigrateAsync` does not downgrade. It sees `[Initial]` as applied, does nothing.
- The DB schema is the `AddFoo` shape, but the code thinks it's the `Initial` shape.
- Runtime behavior: queries that reference columns added by `AddFoo` fail. Queries that don't succeed.

**Decision:** we do not detect or protect against downgrade in v1. The recovery path is "restore from the backup taken during the upgrade that created the newer schema." This is documented in INSTALL.md as part of the recovery procedure.

**Rejected alternative -- schema-version check at startup:** adds complexity for a case we expect to be rare in the OSS-self-hosted model. Revisit if operators surface real pain.

### 13.4 Binary replacement during runtime

Out of scope. The install script stops Collabhost (or assumes it is stopped) before replacing binaries. The process supervisor inside Collabhost doesn't manage Collabhost itself.

---

## 14. Dead Configuration Cleanup -- `Platform:ToolsDirectory`

Per the #159 audit and #153 §17.4: `appsettings.json` declares `Platform:ToolsDirectory = "tools"`. Zero C# readers (`Grep` confirms: only hit is the `appsettings.json` line itself).

### 14.1 Decision -- remove in the same PR that lifts the IsDevelopment gate

The removal is trivial: delete two lines from `appsettings.json`. But it is **not additive** -- an operator with a customized `appsettings.json` that sets `Platform:ToolsDirectory` (hypothetically, for some unknown reason) has that setting silently stop being read.

Since the setting is unused by any code path, the behavioral delta is nil. The risk is aesthetic (someone wrote a non-functional config and notices we deleted it).

### 14.2 Deleting from the shipped `appsettings.json`

Yes. The file after this spec lands:

```json
{
  "ConnectionStrings": {
    "Host": "Data Source=./data/collabhost.db"
  },
  "Auth": {
    "AdminKey": null
  },
  "TypeStore": {
    "UserTypesDirectory": "UserTypes"
  },
  "Proxy": {
    "BaseDomain": "collab.internal",
    "BinaryPath": "",
    "ListenAddress": ":443",
    "CertLifetime": "168h",
    "SelfPort": 58400
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

Changes:
- `ConnectionStrings:Host` default: `./db/collabhost.db` → `./data/collabhost.db` (per #153 §12.2).
- `Platform` section removed entirely (dead).
- `Proxy:BinaryPath` default: `"caddy"` → `""` (per #153 §6.4.1 -- empty means "use the resolver's fallback").

### 14.3 Preservation semantics interaction

Per #153 §9.7, `appsettings.json` is preserved on reinstall. An operator on v0.1.0 with the current file (including `Platform:ToolsDirectory`) upgrades to the v0.2.0 binary. The binary ignores the dead `Platform` section. No breakage. The next operator-edited save of `appsettings.json` is when the section finally disappears.

This is the discipline #153 §9.7 named (every new shipped key needs an in-code default). Here we're doing the inverse -- removing a key that never had a reader. Safe.

---

## 15. Code-Seam Summary

A consolidated view of what changes where. Implementation-only -- this spec does not dictate the PR split; Remy sequences.

| File | Change |
|------|--------|
| `backend/Collabhost.Api/Program.cs` | Remove `IsDevelopment()` gate around migrations + TypeStore + ProxyAppSeeder. Call `StartupPreflight.ValidateAsync` first. Call `MigrationRunner.MigrateWithBackupAsync`. Call `TypeStore.LoadAsync` unconditionally. Call `ProxyAppSeeder.SeedAsync` unconditionally. Call `TypeStore.StartWatching` after both. Keep `MapOpenApi` and `UseCors` gated. Route exits through a central failure-reporter. |
| `backend/Collabhost.Api/Platform/StartupPreflight.cs` (new) | Preflight checks per §5. |
| `backend/Collabhost.Api/Platform/StartupFailure.cs` (new) | Central failure reporter (§12.2). Writes to both `ILogger` and `Console.Error`, sets exit code, terminates. |
| `backend/Collabhost.Api/Data/MigrationRunner.cs` (new) | §6 + §7 encapsulated. Takes backup, applies migrations, returns outcome. |
| `backend/Collabhost.Api/Data/AppTypes/TypeStore.cs` | No contract change. |
| `backend/Collabhost.Api/Proxy/ProxyAppSeeder.cs` | Remove static `ResolveBinaryPath` after #153 Phase 2 lands. Otherwise unchanged. |
| `backend/Collabhost.Api/Authorization/UserSeedService.cs` | Extend to support scenario 3 (configured-key override on non-empty DB). |
| `backend/Collabhost.Api/Authorization/_Registration.cs` | Move admin-key generation from registration's `PostConfigure` into `UserSeedService`. |
| `backend/Collabhost.Api/appsettings.json` | §14.2 edits. |

---

## 16. Test Strategy

Focused -- not a test-plan inventory. Remy sequences within the PRs.

### 16.1 Unit tests (new)

- `MigrationRunnerTests` -- first boot creates DB, normal boot no-ops, pending migrations trigger backup, backup retention rolls at 5, filename-collision exit path.
- `StartupPreflightTests` -- writable dir → ok, unwritable dir → exit code 10, missing user-types dir → created.
- `UserSeedServiceTests` -- add scenario 3 cases (configured key matches existing user → no-op; doesn't match → new admin created).

### 16.2 Integration tests (existing, updated)

- `Collabhost.Api.Tests` -- the WebApplicationFactory fixtures today rely on `IsDevelopment()` triggering seed. Post-#156, they need to rely on the new startup path. Same tests, different harness code.
- `Collabhost.AppHost.Tests` -- Aspire smoke tests should now cover the Production boot path as well (`ASPNETCORE_ENVIRONMENT=Production` variant).

### 16.3 End-to-end migration test (new)

A CI-time test that:
1. Seeds a v0.1.0 schema.
2. Runs the v0.2.0 binary against it.
3. Verifies backup exists, pending migrations applied, data preserved.

Implementation detail for Remy. Suggested as a `[Fact]` in `Collabhost.Api.Tests` using two `AppDbContext` configurations.

---

## 17. Implementation Cost (rough)

Per topic, ballpark T-shirt sizes. Remy refines when he sequences.

| Topic | Size | Notes |
|-------|------|-------|
| §5 Preflight | S | Small new class; no platform branching beyond what path APIs handle. |
| §6 Pre-migration backup | M | File-copy + retention + filename format. Tests take more time than the code. |
| §7 Migration posture (auto + halt-on-fail) | S | Mostly lifting the existing code out of the gate. |
| §8 TypeStore load + watcher in production | S | Remove `IsDevelopment()`; ensure scan dir exists. |
| §9 ProxyAppSeeder in production | XS | Remove `IsDevelopment()`; the seeder is already idempotent. |
| §10 First-run detection | XS | No new code -- callees are idempotent. |
| §11 Admin-key 3-scenario | M | Scenario-3 logic + moving generation into `UserSeedService`. New CLI flag parsing if OQ-3 says yes. |
| §12 Startup failure modes | S | Central reporter + exit-code table. |
| §13 Upgrade story | XS | Already covered by §6 + §7; spec-only. |
| §14 Dead config cleanup | XS | Delete lines. |
| §15 Code-seam PR sequencing | (Remy) | PR 1: preflight + failure reporter. PR 2: migration runner + backup. PR 3: lift gate + TypeStore/seeder in production. PR 4: admin-key scenarios. |

Total rough: **M for design-to-shipped.** Biggest item is the admin-key scenario work + its tests. Migration runner is next. Everything else is plumbing.

Parallelism with #153: Phase 3 (env-var readers) is independent of #156. Phase 2 (CaddyResolver + proxyState) is independent of #156 except for the hook point in §9.2. Phase 4a ships without #156. Phase 4b ships after #156.

---

## 18. Open Questions (Bill rules)

These are the ambiguities I'm deliberately leaving for Bill rather than silently picking. R2 will land whatever he decides.

**OQ-1 -- Backup filename version band.** Do we prefer the applied-migration-name band (`pre-AddActivityEvents-to-AddFoo`) or the semver band (`pre-v0.1.0-to-v0.2.0`)?
- Semver is operator-friendly (they know what "v0.1.0" means).
- Migration name is developer-friendly (matches what EF Core logs on failure).
- Migration name needs `VersionInfo.Current` as a best-effort correlate to be useful. Semver lets the backup speak for itself.
- My lean: **semver** for the operator-facing format, migration name in the `LogCritical` structured fields. Your call.

**OQ-2 -- `--migrate` / `--no-migrate` CLI flags in v1.**
- Argument for: matches `dotnet ef database update`, lets operators script an explicit migrate step.
- Argument against: adds surface, invites "run `collabhost migrate`" scripts that will skip the backup if someone forgets to wrap them.
- My lean: **no -- auto-migrate is the only path in v1.** Revisit when the first operator asks.

**OQ-3 -- Admin-key CLI flag scope in v1.**
- Three potential sources: `Auth:AdminKey` (config, exists), `COLLABHOST_ADMIN_KEY` (env, new), `--admin-key` (CLI, new).
- Ship all three? Ship config + env only and defer CLI to when the first consumer asks?
- My lean: **config + env in v1, CLI deferred.** The CLI flag is valuable for automation but #153 Phase 4b doesn't depend on it; ship it when we ship other CLI flags together (e.g., `--migrate`, `--show-admin-key`).

**OQ-4 -- SIGHUP reload for user types.**
- `FileSystemWatcher` is mostly reliable but has known gaps on network filesystems and some container overlay mounts.
- Adding a SIGHUP handler that triggers `ReloadAsync` costs a few lines.
- My lean: **defer.** No real user has reported FSW failing in Collabhost. Keep the option in the back pocket. Not in v1.

**OQ-5 -- Exit-code table stability.**
- I've picked numbers (10, 11, 20, 30, 31, 40, 50). Once we ship them, operator scripts may depend on them.
- Do we declare the exit codes a stable public contract in INSTALL.md, or say "may change across minor versions"?
- My lean: **document them as informational in v1, don't promise stability yet.** When an operator writes a script that depends on exit code 20, we'll know it's time to freeze.

**OQ-6 -- First-run admin-key recovery mechanism.**
- §11.4 options (a) file on disk, (b) CLI flag, (c) require reset.
- This is #158's territory but #156's migration runner needs to know whether option (a) involves writing a file under `data/` that the migration backup must respect.
- My lean: **defer to #158.** If (a) wins, the migration runner doesn't care -- it only backs up `collabhost.db`, not the whole data directory.

**OQ-7 -- Exit behavior when built-in type validation fails.**
- Exit code 30 halts the process. But an operator on a running install who pulls a broken release is stuck -- the only recovery is to roll back the binary.
- Alternative: soft-fail, log critical, boot in "built-in types broken" mode with a health signal, let the operator roll back via the dashboard.
- Against: a broken built-in type means capability resolution is broken, which probably means `ProcessSupervisor` crashes on first app start anyway.
- My lean: **halt (exit 30).** Broken built-ins ship only on bad releases; the CI smoke tests should catch them before tagging. If one slips through, halting is the right signal.

---

## Appendix A: Cross-references to #153

This spec gates #153 Phase 4b. Where they intersect:

- **#153 §6.4.2 (proxyState):** §9.2 consumes the state machine. No design overlap; we read, they own.
- **#153 §12 (env-var overrides):** §7.2 consumes `COLLABHOST_DATA_PATH`. §8.4 consumes `COLLABHOST_USER_TYPES_PATH`. Resolution lives in the respective `_Registration.cs`; #156 doesn't redefine.
- **#153 §9.7 (installer merge-safe):** §13 depends on this -- `appsettings.json` and `data/` preserved. #156's pre-migration backup lives under `data/backups/`, which inherits the preservation.
- **#153 §18 Phase 4b:** cannot ship until #156 lands. INSTALL.md Troubleshooting content for "if upgrade fails" is owned here; the INSTALL.md file itself ships in Phase 4b.
- **#153 §17.4 (codebase anomalies, TypeStore file-watcher in production):** §8.3 resolves this. Watcher stays on in production; scan dir is created by preflight.
- **#153 §12.3.1 (`Program.cs` env-var precedence fix):** independent of #156. Lands in Phase 3 of #153.

---

## Appendix B: Follow-up Cards Recommended

These are things this spec explicitly parks rather than scoping in. Each is a distinct discussion.

- **#xxx -- CLI flags: `--migrate`, `--no-migrate`, `--show-admin-key`, `--admin-key`.** Per OQ-2 + OQ-3. Ships when #153's CLI surface grows beyond `--version`.
- **#xxx -- Exit-code stability contract.** Per OQ-5. Filed when an operator first writes automation that depends on a specific code.
- **#xxx -- SIGHUP reload for user types.** Per OQ-4. Filed when FSW reliability hurts a real operator.
- **#xxx -- Admin-key file-on-disk vs CLI recovery.** Per §11.4 / OQ-6. #158's territory; this entry is a reminder in case #158 doesn't land before v0.1.0.
- **#xxx -- Backup retention policy revisit.** 5-count is a guess; revisit when we know operator backup sizes (`data/backups/` growing too fast or never used).
- **#xxx -- Downgrade detection.** Per §13.3. File if operators start landing with "I downgraded and now queries fail."
- **#xxx -- Per-route backup / logical snapshot API.** `/api/v1/admin/backup` endpoint for on-demand backups. Uses the SQLite Backup API instead of file copy.
