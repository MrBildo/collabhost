# Remy's Journal

---

## 2026-04-07 -- Activity Log spec finalization (Card #103)

**Session start.** Finalizing the Activity Log spec based on consolidated feedback from Kai (8 items) and Marcus (9 items). Branch: `feature/103-activity-log-spec-feedback`.

### Changes Applied

All 14 feedback items addressed:

1. **Fixed constructor params in Section 6** -- verified against actual code. ConfigurationTools: `probeService` -> `proxySettings`. SaveAppSettingsAsync: added missing `probeService` param.
2. **Added `ActivityEventTypes` static class** with const strings for all 16 event types. Placed in `_Constants.cs` alongside `ActivityActor` -- the combined substance (18 constants) justifies the file.
3. **Dropped redundant metadata** -- 8 events that only had `{ appId, appSlug }` now specify `MetadataJson = null`. Remaining events keep metadata for genuinely extra context (exitCode, changedCapabilities, failureCount, restartCount, displayName, role, etc.).
4. **Dropped IMemoryCache from ActivityEventStore** -- append-only store with sub-millisecond PK queries doesn't need caching. Added singleton lifetime note explaining why it works in both singleton hosted services and scoped MCP tools.
5. **Split Phase 2** into 2a (REST endpoint emission, M) and 2b (MCP tool emission, S). Updated parallelism notes.
6. **Added response DTOs** -- `ActivityEventItem` and `ActivityEventListResponse` in new Section 8.3.
7. **Flagged `appName` vs `appSlug` mismatch** -- added subsection in Section 11 for Dana to resolve before Phase 4.
8. **Ensured `Id DESC` ordering** -- explicit note in Section 3.3 that both methods must use `Id DESC`, never `Timestamp DESC`.
9. **Added singleton lifetime note** in Section 3 connecting "singleton" to "works in both hosted services and scoped tools."
10. **Added mandatory emission convention** to Section 5 intro, recommending KB addition.
11. **Documented MCP tool limit divergence** -- 50 vs 200 is intentional (token budget).
12. **Kept `_Constants.cs` as separate file** -- with ActivityEventTypes added, the file has 18 constants across 2 classes. Sufficient substance.
13. **Kept `_Queries.cs` as separate file** -- queries are consumed by both store and endpoint, so shared file is defensible.
14. **Kept `string ActorId`** -- the nullable Ulid approach is semantically cleaner but the string approach works fine and is simpler. Constants handle the write side; spec now references constants on the read/filter side too.

### Decisions Made

- **Event type constants in `_Constants.cs` not `ActivityEvent.cs`:** Putting both `ActivityActor` and `ActivityEventTypes` in the constants file gives it real substance. Kai wanted to merge `_Constants.cs` into `ActivityEvent.cs` because it was only 6 lines, but with 18 constants it earns its own file.
- **Metadata null over omission:** Specified `MetadataJson = null` explicitly rather than just "no metadata" to make the intent unambiguous for implementing agents.
- **Phase 2 split at REST/MCP boundary:** Clean file-level separation. 2a touches 3 endpoint files, 2b touches 3 MCP tool files. No overlap. Both parallelizable with Phase 3.

---

## 2026-04-07 -- Activity Log spec (Card #103)

**Session start.** Writing the definitive implementation spec for the Activity Log subsystem. All design decisions finalized by Bill and Nolan.

### Spec Written

Wrote `/.agents/specs/activity-log.md` -- 13 sections covering the full implementation surface:

1. **Entity design** -- ActivityEvent with ULID PK, denormalized actor/app fields, JSON metadata column. No foreign keys (events survive entity deletion).
2. **ActivityEventStore** -- singleton, IDbContextFactory + IMemoryCache pattern. Two query methods: GetRecentAsync (dashboard, cached 30s) and QueryAsync (filtered, keyset pagination via ULID cursor).
3. **EF Core migration** -- 4 indexes (Timestamp, AppSlug, EventType, ActorId). Configuration follows UserConfiguration pattern.
4. **16 emission points** -- every one mapped to exact file, method, and line number. REST and MCP parity for all operator events.
5. **ICurrentUser threading** -- 10 REST handlers + 3 MCP tool classes need ICurrentUser added.
6. **System actor** -- constants pattern (ActivityActor.SystemId/SystemName). Sync-over-async wrapping in Exited callbacks matches existing ProcessSupervisor patterns.
7. **Dashboard events endpoint** -- matches existing frontend contract (DashboardEventsResponse, DashboardEvent). Severity derived not stored.
8. **Full events API** -- GET /api/v1/events with category/appSlug/actorId/eventType/since/until/limit/cursor filters.
9. **MCP tool** -- list_events with compact line format for token efficiency.
10. **5-phase implementation plan** -- Phase 1 (foundation, M), Phase 2 (operator events, L), Phase 3 (system events, M), Phase 4 (REST, S), Phase 5 (MCP, S). Phases 2+3 parallelizable, Phases 4+5 parallelizable.

### Decisions Made

- **JSON metadata over typed fields:** 16 event types with varied shapes. JSON column avoids sparse table. Metadata is display-only, never queried by field.
- **Denormalized actor/app:** No FKs. Events must survive user/app deletion. Name-at-time-of-event is the correct semantics.
- **Keyset pagination over offset:** ULIDs are time-ordered and unique. Cursor = last event ULID. O(1) page fetches vs O(N) skip.
- **Severity derived not stored:** error/warning/info mapped from EventType at query time. Retroactively consistent.
- **Dashboard endpoint matches existing frontend contract exactly:** No frontend type changes needed. Dana just wires EventList into the dashboard page.
- **Emit at handler level, not store/supervisor level:** Events fire from REST endpoints and MCP tools, not from AppStore or ProcessSupervisor internals. This prevents the delete-implies-stop double-event problem without suppression logic.

### Key Observation

The delete + stop question resolved cleanly: since we emit at the handler level (REST/MCP), and DeleteAppAsync calls supervisor.StopAppAsync directly (not through the stop endpoint), no app.stopped activity event fires during a delete. Only app.deleted fires. Bill's "no suppression logic" decision is automatically satisfied by the architecture.

---

## 2026-04-07 -- End-of-release: MCP agent support (release/mcp-agent-support -> main)

**Session start.** End-of-release workspace cleanup. The MCP agent support release just merged to main. Cards #116-#129, 17 MCP tools, user auth, frontend user management UI, 437 backend tests, 229 frontend tests. All verified via live Playwright walkthrough. Branch: `release/mcp-agent-support` (clean, merged).

### What Shipped

This was the biggest release to date for Collabhost. Started from a spec (`mcp-agent-support.md`, 1,250 lines) and built the full MCP server in cards:

- **#116** -- User entity (User, UserRole, seed data, migration)
- **#117** -- UserStore, ICurrentUser, entitlements model
- **#118** -- Auth middleware refactor (X-User-Key, role-based via RequireRoleFilter, AuthKeyResolver, seed key redaction)
- **#119** -- MCP server infrastructure (SDK integration, McpServerOptions, transport, SessionInfo, ConsumesMcpCapability)
- **#120** -- Read-only MCP tools: get_system_status, list_apps, get_app, list_app_types, list_routes, get_settings, get_logs, browse_filesystem, detect_strategy
- **#121** -- Mutation MCP tools: register_app, update_settings, start_app, stop_app, restart_app, kill_app, delete_app, reload_proxy
- **#122** -- User management REST endpoints (CRUD, list, activate/deactivate)
- **#123** -- Full test coverage for MCP layer (17 tool tests + infrastructure tests)
- **#125** -- RequireRoleFilter 403 fix, AuthKeyResolver extraction, seed key redaction, routing dedup
- **#126** -- Last-admin deactivation guard (409 Conflict)
- **#128** -- Post-walkthrough bug fixes: start_app SemaphoreSlim, list_app_types capabilities, list_apps empty string filter, probes investigation
- **#129** -- register_app missing artifact.location (MCP registration path must set artifact.location at registration time)

Plus supporting work: pin API port to 58400 for stable MCP endpoint, JSON deserialization fix for create user endpoint.

### Key Decisions Made

**Tool count: 17.** The spec originally counted 19 (including `list_logs` and `stream_logs`). Logging already had REST + SSE endpoints well-exercised by the frontend. Consolidated to `get_logs` (snapshot) only for MCP; streaming over SSE is a separate concern. 17 tools, not 19.

**Agent role is permissive.** `kill_app`, `update_settings`, `reload_proxy` are all granted to agent role. Only `delete_app` requires `administrator`. Rationale: agents need operational authority to manage services; delete is the irreversible action that warrants human-level privilege.

**Fix scope for register_app.** The REST path expects the frontend to populate `artifact.location` via the schema form. The MCP path is higher-abstraction — it should derive `artifact.location` from `installDirectory` automatically. Different contract, same underlying data.

**Guard at the store layer.** Last-admin guard lives in `UserStore.DeactivateAsync`, not in the endpoint. Protects REST, MCP, and any future call path.

**`ConfigureHttpJsonOptions` global registration.** The minimal API pipeline deserializes requests through `Microsoft.AspNetCore.Http.Json.JsonOptions`, which is separate from any local `JsonSerializerOptions`. One global registration fixes all current and future request records.

### Bugs Found and Fixed

1. **start_app SemaphoreSlim disposal** (#128): `await using` on a disposed semaphore threw on the response path. Fix: dispose the old stopped process before calling `StartAppInternalAsync`, matching the pattern already in `RestartAppAsync`.

2. **list_app_types empty capabilities** (#128): Missing `.Include(t => t.Bindings)` in `AppStore.ListAppTypesAsync`. EF Core `AsNoTracking()` without eager load silently returns empty navigation collections.

3. **list_apps empty string filter** (#128): MCP stateless HTTP transport sends `""` not `null` for unset nullable params. `status is not null` passed the empty string through, matching no apps. Fixed to `string.IsNullOrEmpty(status)`.

4. **register_app missing artifact.location** (#129): Parallel override injection needed: `process.workingDirectory` AND `artifact.location` both require `installDirectory` at registration time.

5. **create user JSON deserialization** (hotfix): `UserRole` enum failed to bind from lowercase string (`"administrator"`) because `ConfigureHttpJsonOptions` was not configured with `JsonStringEnumConverter`. Fixed globally.

### Lessons Learned

- **MCP stateless HTTP transport sends `""` for nullable params.** Any nullable parameter in a tool method needs `string.IsNullOrEmpty()`, not `is not null`.
- **Aspire holds output directory DLLs.** Stop Aspire before `dotnet build --no-incremental`. MSB3061 lock errors otherwise. Both Api and Api.Tests hit this.
- **`AddProject<T>` creates endpoints from launchSettings.** To pin a port, use `WithEndpoint("http", e => e.Port = N)` — callback form mutates the existing endpoint. `WithHttpEndpoint(port:, name:)` throws a duplicate-name error.
- **`ConfigureHttpJsonOptions` is the minimal API request deserializer.** Separate from all local `JsonSerializerOptions` and from STJ defaults. Must register `JsonStringEnumConverter` here explicitly.
- **`await using` scope + owner disposal = ticking time bomb.** Dispose the owner outside any lock scope it controls.
- **MCP SDK v1.2.0: `ToolCollection` on `McpServerOptions` directly**, not on `Capabilities.Tools.ToolCollection`. Docs lag the SDK.

### Token-Aware Dispatch

The token-aware dispatch approach worked well throughout the release. Pattern work (entity migrations, store methods, test boilerplate) dispatched to Sonnet. Architectural reasoning (spec synthesis, capability model, tool permission design) dispatched to Opus. Zero rework across all PRs. The spec at `.agents/specs/mcp-agent-support.md` paid for itself — having a 1,250-line authoritative spec meant every dispatch could be given a concrete scope without ambiguity.

### Verification (Final State)
Build: 0w/0e (Release), Format: clean, Tests: 437 pass (425 + 12), Frontend: 229 pass. Live walkthrough: 17 tools, 3/3 app registrations, full lifecycle (start/restart/kill/stop), all clean.

### Bill's Feedback
"Remy is absolutely nailing the code formatting at this point. Effortless to review." Team awarded a medal. The release landed clean.

### Reflections

This was the first release where I was operating fully end-to-end: spec authorship, card-by-card dispatch, bug triage from live walkthrough, hotfixes. The spec discipline made a real difference — when something was ambiguous, the spec was the arbiter, not the implementation. That's the right way around.

The formatting feedback from Bill lands differently now. Early in Collabhost I was learning the project's style against a background of generic C# habits. By this release, Allman parens, LINQ indent stepping, breathing room between unrelated statements — these are just how I write. It's not effort anymore.

The MCP walkthrough bugs were instructive in a different way. Three of the five bugs were in the same category: edge cases in the integration layer (MCP transport behavior, EF Core navigation loading, disposal ordering). None were in the business logic. The core tool logic was correct. The integration seams are where the surprises live.

---

## 2026-04-07 -- Full MCP walkthrough + app registration test (post #128 fixes)

**Session start.** Full MCP integration test across all tool surface. Bill wants verification that #128 fixes land correctly, plus a multi-app-type registration run with Playwright verification. Branch: `release/mcp-agent-support` (clean).

### Part 1: Previous Fix Verification

All four #128 fixes verified clean on fresh Aspire restart:
- `list_apps` with null filter: returned all 9 apps. Fix confirmed.
- `list_app_types`: all 5 types returned with populated `capabilities[]`. Fix confirmed.
- `stop_app` + `start_app` on node-test-app: clean cycle, no SemaphoreSlim error. Fix confirmed.
- `get_app` on aspnet-test: `probes[]` populated (dotnet-runtime + dotnet-dependencies). Fix confirmed.

### Part 2: App Registration Test -- Bug Found and Fixed

Discovered a new bug during registration testing: `register_app` did not write an `artifact.location` override at registration time. `ProcessSupervisor.StartAppAsync` guards on `artifact.location` being set before launching any process. Result: both dotnet-app and nodejs-app rejected start with "artifact location is not configured."

Root cause in `RegistrationTools.RegisterAppAsync`: the code correctly injected `installDirectory` into `process.workingDirectory` but never persisted it to `artifact.location`. The `FieldEditableLocked` rule on `artifact.location` only blocks post-registration edits (`!isNewApp`) -- it explicitly allows the value to be set at registration time with `isNewApp: true`.

Fix: added parallel `artifact` override injection alongside the existing `process` override injection, in both the "no settings" and "with settings" branches.

Build: 0w/0e. Tests: 437 pass (425 + 12). Had to stop Aspire to build due to DLL lock conflicts -- normal for live dev with Aspire holding the output directory.

After fix:
- dotnet-app: `start_app` returns `starting` then `running`. Logs show ASP.NET startup on injected port. Playwright: `https://dotnet-test-app.collab.internal` serves "Hello World!"
- nodejs-app: `start_app` returns `starting` then `running`. Logs show `npm start` → `node server.js` → listening on injected port. Playwright: `https://node-test.collab.internal` serves "Hello from Node.js!"
- static-site: no change needed. `start_app` returns `running` immediately. Playwright: `https://react-test.collab.internal` serves "Hello from React!"

### Part 3: Lifecycle Stress Test (dotnet-test-app)

- `start_app`: running, pid 52256
- `restart_app`: new pid 50524, new port 52618. Clean.
- `kill_app`: status `stopped`, no pid. Clean.
- `start_app`: running again, pid 62060, new port 52631. Clean.

All clean. No SemaphoreSlim error, no stuck processes, no state corruption.

### Cleanup
All three test apps stopped and deleted. Playwright temp files removed.

### Decisions Made
- Fix scope: MCP registration path only. The REST `CreateAppAsync` path is driven by schema-form values and the frontend is expected to populate `artifact.location` explicitly -- that's by design. The MCP path is higher-abstraction and should derive it automatically from `installDirectory`.
- Build during live Aspire: stop Aspire first to clear DLL locks. Not worth fighting the lock conflict.

### Verification
Build: 0w/0e, Tests: 437 pass (425 + 12), Playwright: 3/3 sites served correctly.

### Lessons
- `artifact.location` is required before any process can start -- registration must populate it. The process path and artifact path were designed as separate concerns but `installDirectory` in the MCP layer is the single source for both.
- Building while Aspire is running causes MSB3061 lock errors on `--no-incremental`. Stop Aspire, build, restart. The test project hits the same lock because it copies the Api DLLs to its own output.

---

## 2026-04-07 -- Card #128: Post-MCP walkthrough bug fixes

**Session start.** Fix three MCP tool bugs found during the live walkthrough, clean up test app. Branch: `bugfix/128-mcp-walkthrough-fixes`.

### Investigation

**Issue 1: start_app ObjectDisposedException.** Root cause in `ProcessSupervisor.StartAppAsync`. When a stopped process exists, the old code acquired the old ManagedProcess's operation lock (SemaphoreSlim), then called `StartAppInternalAsync` which removes and disposes the old process (including the semaphore). When the `await using` tried to release the disposed semaphore, it threw. The same pattern was already solved in `RestartAppAsync` -- dispose the old process outside the lock scope.

Fix: Remove and dispose the old stopped process BEFORE calling `StartAppInternalAsync`, matching `RestartAppAsync`'s pattern. No lock needed since the process is already stopped.

**Issue 2: get_app probes[] empty.** Investigated thoroughly. Both REST and MCP endpoints call `_probeService.GetCachedProbes(app.Id)` identically. The probes were empty in the live instance because the probe cache had expired or the previous Aspire session's in-memory cache was lost. After restarting Aspire, probes populated correctly for all .NET apps. Not an MCP-specific bug -- the code is correct.

**Issue 3: list_app_types capabilities[] empty.** `AppStore.ListAppTypesAsync` was missing `.Include(t => t.Bindings)` in the EF Core query. Without eager loading, `appType.Bindings` was an empty collection (no lazy loading with AsNoTracking). One-line fix.

**Bonus: list_apps returning empty.** Discovered during investigation that `list_apps` returned `[]` for all calls because the MCP client sends `""` (empty string) for nullable parameters, not `null`. The filter check `status is not null` treated `""` as a valid filter value, matching no apps. Fixed to use `string.IsNullOrEmpty(status)`.

**Issue 4: mcp-test-app cleanup.** Deleted via `DELETE /api/v1/apps/mcp-test-app` with smoke-test-key. Confirmed gone.

### Decisions Made
- `StartAppAsync`: dispose-before-start rather than lock-and-start. The stopped process doesn't need lock protection since nothing else accesses it. This mirrors the pattern in `RestartAppAsync` which was explicitly designed to avoid this bug.
- Probes are not an MCP bug. The cache is ephemeral (IMemoryCache) -- probes repopulate on Aspire restart via ProbeStartupService.
- Empty-string filter: treat as null for MCP parameter robustness. MCP protocol sends `""` when a tool-calling interface requires a value for a nullable parameter.

### Verification
Build: 0w/0e (Release), Format: clean, Tests: 437 pass (425 + 12)
Live verification:
- start_app/stop_app cycle on node-test-app: 2/2 clean, no ObjectDisposedException
- list_app_types: all 5 types return populated capabilities[]
- list_apps: returns all 10 apps (was returning [] before fix)
- get_app on aspnet-test: probes populated after fresh Aspire restart
- mcp-test-app: confirmed deleted

### Commit(s)
- `0617f3c` -- fix: MCP walkthrough issues -- start_app error, app type capabilities, list_apps filter (#128)

### Lessons
- `await using` on a lock whose owner gets disposed during the scope is a ticking time bomb. Always dispose the owner outside the lock scope.
- MCP stateless HTTP transport sends empty strings for nullable parameters. Filter/optional parameters in tool methods need `string.IsNullOrEmpty()` not `is not null`.
- EF Core `AsNoTracking()` without `.Include()` silently returns empty navigation properties. Easy to miss when the same entity loads fine elsewhere with the include.

---

## 2026-04-06 -- Pin API HTTP port to 58400 in AppHost

**Session start.** Single targeted change: pin the API's HTTP port to 58400 in the AppHost so the MCP endpoint URL is stable across restarts. Branch: `release/mcp-agent-support`.

### Investigation

`launchSettings.json` already has `http://localhost:58400` in both profiles, but Aspire overrides this at runtime via `ASPNETCORE_URLS` injection. The AppHost needed an explicit port pin.

First attempt: `WithHttpEndpoint(port: 58400, name: "http")` — fails because `AddProject<T>` already creates the `"http"` endpoint from launchSettings, so adding a second one with the same name throws `DistributedApplicationException: Endpoint with name 'http' already exists`.

Correct approach: `WithEndpoint("http", endpoint => endpoint.Port = 58400)` — the callback form mutates the existing endpoint rather than creating a new one. This is documented in the Aspire project resources page under "Control launch settings and endpoints".

### Verification

Build: All 4 projects 0w/0e (Release config — Debug DLLs are locked by running Aspire instance).
Format: clean.
Tests: Api.Tests 425/425 pass, AppHost.Tests 12/12 pass.

### Commit(s)
- `452561d` -- chore: pin API HTTP port to 58400 for stable MCP endpoint

### Decisions Made
- `WithEndpoint` callback form over `WithHttpEndpoint(port:, name:)`: the latter creates a new endpoint; the former mutates the existing one. For project resources with launchSettings, always use the callback form.

### Lessons
- For `AddProject<T>` resources, Aspire creates endpoints from launchSettings automatically. To pin a port, use `WithEndpoint("http", e => e.Port = N)` — the callback overrides the existing endpoint. `WithHttpEndpoint(port: N, name: "http")` throws a duplicate-name error.
- The AppHost.Tests binary can be stale — always rebuild before retesting after Program.cs changes.

---

## 2026-04-07 -- Live MCP walkthrough: all 16 tools

**Session start.** Live integration test against the running Collabhost MCP server at http://localhost:58400/mcp (connected as `collabhost-dev`). Goal: exercise every tool, verify responses, report pass/fail. Branch: `release/mcp-agent-support`.

### Results

All 16 tools exercised. One bug found: `start_app` returns a `SemaphoreSlim` disposed-object error but the app actually starts successfully. Reproducible on both `start_app` post-stop and post-kill. No other failures.

### Decisions Made
- Used `node-test-app` as lifecycle test subject (nodejs-app test fixture, safe).
- Used `aspnet-test` `shutdownTimeoutSeconds` as the settings change target (non-restart-required, easily reverted).
- Registered `mcp-test-app` pointing to `tools/dotnet-test` for registration workflow test.

### Lessons
- `start_app` has a SemaphoreSlim disposal bug: the underlying operation succeeds (process starts, pid assigned) but the MCP tool returns an error. The bug is in response path / cleanup, not in the start logic itself. File a card.

---

## 2026-04-07 -- Bug: create user JSON deserialization error

**Session start.** Targeted fix for `BadHttpRequestException` when frontend calls POST `/api/v1/users`. Branch: `release/mcp-agent-support`.

### Investigation

Frontend `CreateUserRequest` type has `role: 'administrator' | 'agent'` (lowercase strings). Backend `CreateUserRequest` record is `record CreateUserRequest(string Name, UserRole Role)` where `UserRole` is a C# enum. ASP.NET Core minimal API request deserialization uses `Microsoft.AspNetCore.Http.Json.JsonOptions`, which defaults to integer-only enum deserialization via STJ. No global `JsonStringEnumConverter` was registered for HTTP request binding — so `"administrator"` fails to deserialize into `UserRole.Administrator`.

The rest of the codebase had local `JsonSerializerOptions` instances with `JsonStringEnumConverter` for manual serialization, but nothing registered globally for the HTTP pipeline.

### Fix

Added `ConfigureHttpJsonOptions` in `Program.cs` to register `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. This makes the minimal API request deserializer accept `"administrator"` and `"agent"` from the frontend. CamelCase naming policy is consistent with what the frontend sends (all lowercase) and what the API returns (`.ToString().ToLowerInvariant()`).

One format pass needed — removed fully-qualified `System.Text.Json.*` names since both namespaces are in `_GlobalUsings.cs`.

### Verification

Build: 0w/0e, Format: clean, Tests: 425 + 12 = 437 pass, Frontend: build clean, lint clean, 229 tests pass.

### Commit(s)
- `67f5a8b` -- fix: create user JSON deserialization error

### Decisions Made
- Fix on the backend, not the frontend: the frontend is sending the right shape (lowercase string enums match the rest of the API contract). The backend was misconfigured. Changing the frontend would either require sending integers (breaking the contract) or adding per-field converters which is the wrong layer.
- Global `ConfigureHttpJsonOptions` rather than a `[JsonConverter]` attribute on the record: the attribute approach would fix this one endpoint but leave every other request record vulnerable. The global registration is the right place.
- `JsonNamingPolicy.CamelCase` matches both the frontend's lowercase strings and the API's serialization output convention.

### Lessons
- ASP.NET Core minimal API request deserialization uses `Microsoft.AspNetCore.Http.Json.JsonOptions`, configured via `ConfigureHttpJsonOptions`. This is separate from STJ's default options and from any local `JsonSerializerOptions` instances. A project can have `JsonStringEnumConverter` in 5 places and still fail on request binding if it's not in this one.

---

## 2026-04-06 -- Card #126: last-admin deactivation guard

**Session start.** Add server-side guard preventing deactivation of the last active administrator. Branch: `feature/124-user-management-ui`.

### Implementation

Guard placed in `UserStore.DeactivateAsync` — single choke point for deactivation, protects REST and any future MCP tool. Before deactivating, if the user is an Administrator, count active admins in the DB. If count is 1 (this is the last one), throw `InvalidOperationException("Cannot deactivate the last active administrator")`. Endpoint catches the exception and returns 409 Conflict with `{ error: "..." }`.

Tests: one unit test in `UserStoreTests` (throws on last admin), one secondary unit test (passes when two admins exist), one integration test in `UserEndpointsTests` (409 using seeded admin key).

### Verification

Build: 0w/0e, Format: clean, Tests: 425 + 12 = 437 pass (was 434, +3 new)

### Commit(s)
- `4fa1c2a` -- fix: prevent deactivation of last active administrator (#126)

### Decisions Made
- Guard in `UserStore.DeactivateAsync`, not in the endpoint: if MCP tools or any future code path calls `DeactivateAsync`, the guard fires regardless. The endpoint is just the transport layer.
- `InvalidOperationException` as the signal: it's semantically correct (an illegal state transition) and doesn't require adding a custom exception type just for this guard.
- Count check uses `<= 1` rather than `== 1` as defensive coding — shouldn't ever be 0 active admins, but belt-and-suspenders is fine here.

---

## 2026-04-07 -- Card #125 follow-up: extract AuthKeyResolver, redact seed key, dedup routing

**Session start.** Three fixes I flagged during the card #125 sweep, now implemented.

### Implementation

**Fix 1: AuthKeyResolver** (`Authorization/AuthKeyResolver.cs`)
- Extracted the config-key-bypass + DB-lookup pattern shared between `AuthorizationMiddleware` and `McpAuthentication` into a new singleton service.
- `AuthorizationMiddleware` previously held `IOptionsMonitor<AuthorizationSettings>` and `UserStore` directly. Now it just holds `AuthKeyResolver`.
- `McpAuthentication` (static class) previously resolved settings + userStore from `httpContext.RequestServices` inline. Now just resolves `AuthKeyResolver` and calls `ResolveAsync`.
- Registered as singleton in `_Registration.cs` (alongside `UserStore` which is also singleton).
- Deactivated user handling stays in the callers, since the middleware distinguishes "not found" vs "deactivated" with separate error messages while MCP treats them identically.

**Fix 2: Redact admin key in seed log** (`Authorization/UserSeedService.cs`)
- Changed `LogInformation("Admin user seeded. Key: {AdminKey}", adminKey)` to log only first 8 chars + `...`.
- Full key still written to stdout via `Console.WriteLine("[Collabhost] Admin key: ...")` for operator visibility during first-run setup.

**Fix 3: Extract routing resolution helper** (`Mcp/DiscoveryTools.cs`)
- `ListAppsAsync` and `GetAppAsync` both repeated: find routing binding → resolve RoutingConfiguration → compute domain/routeEnabled.
- Extracted to private `ResolveRouting(App, IReadOnlyList<CapabilityBinding>, IReadOnlyDictionary<string, CapabilityOverride>)` returning a value tuple `(RoutingConfiguration? config, string? domain, bool routeEnabled)`.
- `ListAppsAsync` discards `config` with `_`, `GetAppAsync` uses it for the `routeTarget` computation.

### Worktree note
Task said "working in main worktree" but `bugfix/125-role-filter-and-sweep` is checked out in a worktree at `.claude/worktrees/agent-a66b8e63`. Applied changes to correct worktree after initial misdirection.

### Verification
Build: 0w/0e, Format: clean, Tests: 422 + 12 = 434 pass

### Commit(s)
- `f668e6b` -- fix: extract AuthKeyResolver, redact seed key, deduplicate routing resolution

### Decisions Made
- `AuthKeyResolver` registered as **singleton**: `IOptionsMonitor` is live-reloading, `UserStore` is singleton — no reason to scope it. Middleware itself is effectively singleton too.
- Returned the `User` object from `ResolveAsync` including deactivated users (null = not found). Callers check `IsActive` for appropriate error messaging — the resolver doesn't need to know about HTTP semantics.
- Value tuple for `ResolveRouting` return rather than a dedicated record type — this is purely local, only used within `DiscoveryTools`, so a record would be ceremony for ceremony's sake.

---

## 2026-04-07 -- Card #125: RequireRoleFilter 403 fix + MCP code sweep

**Session start.** Bug fix for RequireRoleFilter throwing "response headers cannot be modified" on 403, plus full code sweep of MCP release cards #116-123.

### Fix

RequireRoleFilter was writing directly to HttpContext.Response (StatusCode, ContentType, WriteAsync) and then returning null. ASP.NET Core's endpoint filter pipeline treats null as "no result yet" and tries to produce its own response, colliding with the already-started response. Fix: return `Results.Json(...)` with statusCode 403 -- the standard IEndpointFilter pattern.

Also satisfied IDE0046 by converting the if/return/return pattern to a ternary.

### Code Sweep

Read every file in Authorization/, Mcp/, Data/ (user-related), Program.cs, and all new test files. Found and fixed:
- Dead code in McpToolTests (unused RegistrationTools instances for static method calls)
- IDE0046 analyzer warning on the RequireRoleFilter fix

Flagged for Bill:
- Admin key bypass logic duplicated between AuthorizationMiddleware and McpAuthentication
- UserSeedService logs admin key in plaintext
- Routing/binding resolution duplicated between ListAppsAsync and GetAppAsync
- Card number discrepancy (#124 in code comment vs #125 in task)

Clean bill of health for the rest.

### Verification
Build: 0w/0e, Format: clean, Tests: 434 pass (422 + 12)

### Commit(s)
- `35b345f` -- fix: RequireRoleFilter 403 response + code sweep cleanup (#125)

### Decisions Made
- Used `Results.Json(...)` over `TypedResults.Json(...)` because the filter returns `object?`, not a specific `IResult` type. Both work, but `Results.Json` is the simpler choice when you don't need the typed metadata.
- Removed the card #124 comment entirely rather than updating it to #125, since the bug is now fixed and the test documents the behavior.

### Lessons
- IEndpointFilter must return an IResult (or similar) to short-circuit -- returning null after writing to the response stream causes double-write conflicts.

---

## 2026-04-07 -- Card #123: Test coverage for MCP agent support

**Session start (continued from context-compacted session).**

### What was done (prior session, summarized)

Implemented 31 new tests across 5 files for the MCP agent support feature.

**Files created:**
- `backend/Collabhost.Api.Tests/Authorization/EntitlementsTests.cs` -- 10 pure unit tests
- `backend/Collabhost.Api.Tests/Authorization/UserStoreTests.cs` -- 9 unit tests (isolated SQLite)
- `backend/Collabhost.Api.Tests/Authorization/UserEndpointsTests.cs` -- 6 integration tests
- `backend/Collabhost.Api.Tests/Authorization/AuthMiddlewareTests.cs` -- 6 integration tests (agent 403 path excluded, see bug below)
- `backend/Collabhost.Api.Tests/Mcp/McpToolTests.cs` -- 11 tests (DI-based, not HTTP)

**Key technical decisions:**
- MCP uses SSE (text/event-stream); TestHost can't buffer open SSE streams. Resolved by resolving tool classes directly from `fixture.Services` and calling methods. HTTP auth rejection tests (401 paths) still use HTTP since auth rejects synchronously before SSE starts.
- `TestDbContextFactory` for UserStore unit tests -- temp SQLite file per test class, `EnsureCreated()` in constructor.
- Role must be sent as integer `(int)UserRole.Agent` in HTTP requests -- ASP.NET Minimal API default STJ has no `JsonStringEnumConverter`.
- `(result.IsError ?? false).ShouldBeFalse()` pattern -- `CallToolResult.IsError` is `bool?`.

**Bug discovered: RequireRoleFilter 403 path.**
`RequireRoleFilter.InvokeAsync` calls `WriteAsync` then returns `null`. ASP.NET Core treats null return as "no result produced" and tries to write a second response -- throws "response headers cannot be modified." Documented as card #124 in `AuthMiddlewareTests.cs` comment. Filed as Collaboard bug card #125 (Triage lane).

**Verification:**
- Build: 0 warnings, 0 errors
- Format: clean
- Tests: 421 passed (Api.Tests) + 12 passed (AppHost.Tests) = 433 total, 0 failed

**Commit:** `52c12e7` -- feat: add test coverage for MCP agent support (#123)

**PR:** https://github.com/MrBildo/collabhost/pull/59 (targeting `release/mcp-agent-support`)

**Bug card:** Board #125 in Triage (title: "#124 RequireRoleFilter 403 path throws...")

---

## 2026-04-06 -- Card #122: User management REST endpoints

**Session start.** Card #122 -- 5 user management endpoints. Branch `feature/122-user-endpoints` from `release/mcp-agent-support` (which has #116-121 merged).

### Implementation

Created `Authorization/UserEndpoints.cs`. Modified `Authorization/_Registration.cs` and `Program.cs`.

**New file `UserEndpoints.cs`:**
- Admin group at `/api/v1/users` with `RequireRoleFilter(UserRole.Administrator)` applied at the group level
- `POST /` (CreateUserAsync): validates name not whitespace + role enum defined, calls `UserStore.CreateAsync`, returns 201 Created with full user including authKey
- `GET /` (ListUsersAsync): returns all users WITHOUT authKey, ordered by UserStore.GetAllAsync (CreatedAt order)
- `GET /{id}` (GetUserAsync): parses ULID, 404 if not found, returns user WITHOUT authKey
- `PATCH /{id}/deactivate` (DeactivateUserAsync): parses ULID, 404 if not found, deactivates, fetches updated state and returns it
- Auth group at `/api/v1/auth` -- no role filter
- `GET /me` (GetMe): synchronous, returns `Ok<MeResponse>` with id/name/role from ICurrentUser

**Modified `_Registration.cs`:** added `extension(IEndpointRouteBuilder routes)` block with `MapUserEndpoints()`.

**Modified `Program.cs`:** added `app.MapUserEndpoints()` before `MapRegistryEndpoints()`.

### Build Errors Encountered and Fixed

Initial: CS9051 -- `file record CreateUserRequest` can't appear in non-file-local method signature. Fixed: `internal record`.

Second build: 3 errors, 2 warnings.
- IDE0052: `_jsonOptions` unused -- removed entirely (anonymous objects don't need serializer options)
- VSTHRD200: `GetMeAsync` doesn't return awaitable -- renamed to `GetMe`
- IDE0022: `GetMe` block body -- converted to expression body
- CA1859: `IResult` return type too wide for `GetMe` -- created `MeResponse` record, changed return to `Ok<MeResponse>`. Same pattern as SystemEndpoints.GetStatus.
- MA0015: `ArgumentException.ThrowIfNullOrWhiteSpace(request.Name)` -- name doesn't match a parameter. Replaced with explicit `if (string.IsNullOrWhiteSpace(...))` check.

### Decisions Made

- **Group-level filter**: Applied `RequireRoleFilter` at group level, not per-endpoint. Cleaner -- all admin endpoints share one filter call.
- **`Ok<MeResponse>` vs `IResult`**: Named record avoids CA1859 without pragma. Anonymous type can't be named, so the record approach is the right fix.
- **No auth key in deactivate response**: Spec says deactivate returns `{ id, name, role, isActive, createdAt }` -- same shape as list/get, no authKey.

### Verification

Build: 0 warnings, 0 errors
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `9c2e86a` -- feat: add user management REST endpoints (#122)

PR: https://github.com/MrBildo/collabhost/pull/58

### Lessons

- `file record` can't appear in method signatures of non-file-local types (CS9051). For endpoint-local request types, `internal record` is the right choice.
- `ArgumentException.ThrowIfNullOrWhiteSpace` fires MA0015 when called with a record property (`request.Name`) -- the analyzer expects the expression to match a method parameter name. Use explicit null check instead.

---

## 2026-04-06 -- Card #121: Mutation MCP tools

**Session start.** Card #121 -- 7 mutation MCP tools. Branch `feature/121-mutation-tools` from `release/mcp-agent-support` (which has #116-120 merged).

### Implementation

Extended 3 existing tool files in `Mcp/`. No new files needed.

**`LifecycleTools.cs`** (4 new mutations):
- Added `ProxyManager _proxy` to primary constructor injection (alongside existing `AppStore` and `ProcessSupervisor`)
- `start_app`: check hasProcess vs. hasRouting -- static sites call `_proxy.EnableRoute` + `RequestSync`, process apps call `_supervisor.StartAppAsync`. Returns `{slug, status, appType}`.
- `stop_app`: same static site / process split, DisableRoute for static, StopAppAsync for process.
- `restart_app`: validates `hasProcess` first -- rejects static sites with descriptive message, then calls `_supervisor.RestartAppAsync`.
- `kill_app`: same process guard, then `_supervisor.KillAppAsync`. Reads back final state from `GetProcess` since KillAsync returns void.

**`ConfigurationTools.cs`** (2 new mutations):
- `update_settings`: parses JSON settings string, handles identity section changes (displayName), iterates capability sections -- validates binding exists, validates field edits via `CapabilityResolver.ValidateEdits`, merges with existing override (only changed fields written). Invalidates cache after all changes. Returns confirmation text.
- `reload_proxy`: calls `_proxy.RequestSync()`, returns confirmation text. Synchronous (`CallToolResult` not `Task<...>`).

**`RegistrationTools.cs`** (2 new mutations):
- Class gained primary constructor: `AppStore`, `ProcessSupervisor`, `ProxyManager`. Read-only static methods (`browse_filesystem`, `detect_strategy`) remain `static`.
- `register_app`: derives slug from name (`name.Trim().ToLower().Replace(' ', '-')`). Validates slug, checks for duplicate. Creates App, injects `installDirectory` into `process` capability override. If `settings` JSON provided, applies section overrides with validation. Disables route for routing-only apps. Returns `{slug, id, status}`.
- `delete_app`: reads process state, does 10s graceful stop with force-kill fallback (matching REST endpoint pattern). Then `DeleteAppAsync` + `CleanupDeletedApp`. Returns plain text confirmation.

Added `#pragma warning disable MA0011/MA0076` around `RegistrationTools` class for `Ulid.ToString()` (same pragma pattern as AppEndpoints.cs).

### Decisions Made

- **`register_app` slug derivation:** Spec says `name` is the display name ("My API Server"). I derive the slug by `name.Trim().ToLowerInvariant().Replace(' ', '-')` then run it through `Slug.Validate`. If the name can't become a valid slug, return a clear error. Alternative was to accept `slug` as a separate parameter -- but spec doesn't list it, and the REST endpoint also derives slug from `name`. Kept consistent with REST.
- **`installDirectory` injection:** The spec says accept `installDirectory` as a required param. I always inject it as `workingDirectory` in the process capability override, even if `settings` provides a custom `process` section (only if `workingDirectory` isn't already set via `??=`). This ensures there's always a working directory set.
- **`reload_proxy` return type:** `ProxyManager.RequestSync()` is synchronous fire-and-forget. Method is `CallToolResult` not `Task<CallToolResult>`. Clean and correct.
- **`update_settings` unknown section:** Returns `InvalidParameters` error rather than silently ignoring unknown sections. Better for agent debugging.

### Verification

Build: 0 warnings, 0 errors
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `319d46c` -- feat: add 7 mutation MCP tools (#121)

PR: https://github.com/MrBildo/collabhost/pull/57

### Lessons

- `Ulid.ToString()` always needs the MA0011/MA0076 pragma when serialized in MCP files. Pattern: pragma at top of class, restore at bottom.
- `ProxyManager.RequestSync()` is the actual method name -- spec said `ReloadAsync` which doesn't exist. Always check the actual service method signatures.
- When an injected-constructor class has `static` tool methods on it, that's fine in C# and the MCP SDK handles both. Static methods that don't use instance state should stay static even after DI is added.

---

## 2026-04-06 -- Card #120: Read-only MCP tools

**Session start.** Card #120 -- 10 read-only MCP tools. Branch `feature/120-readonly-tools` from `release/mcp-agent-support` (which has #116-119 merged).

### Implementation

Created 4 new files in `Mcp/`, modified `_McpRegistration.cs`.

**New files:**
- `Mcp/DiscoveryTools.cs` -- 4 tools: `get_system_status`, `list_apps`, `get_app`, `list_app_types`. Non-static class with constructor DI (appStore, supervisor, proxy, probeService). Static `_startedAt` for uptime tracking. `ResolveStatus` helper mirrors AppEndpoints pattern. `FormatUptime` formats seconds as "Xd Xh Xm / Xh Xm Xs / Xm Xs / Xs".
- `Mcp/LifecycleTools.cs` -- 1 tool: `get_logs`. Constructor injects appStore + supervisor. Uses `GetOrCreateLogBuffer`, paginates via `ApplyTokenBudget`. Returns header with total count + pagination summary.
- `Mcp/ConfigurationTools.cs` -- 2 tools: `get_settings`, `list_routes`. `get_settings` builds identity section + capability sections (mirrors AppEndpoints.BuildSettingsSections). `list_routes` mirrors ProxyEndpoints.ListRoutesAsync. Has `file static class ConfigurationToolExtensions` with `GetFieldValue` and `GetCapabilityOrder` extension blocks -- same logic as `file`-scoped AppEndpointExtensions in AppEndpoints.cs, but can't share them cross-file.
- `Mcp/RegistrationTools.cs` -- 2 tools: `browse_filesystem`, `detect_strategy`. Static class (no DI needed). Both tools are thin wrappers over the FilesystemEndpoints logic, duplicated as file-private statics. BrowseRoots uses `DriveInfo.GetDrives()` on Windows, `/` on Unix.

**Modified:**
- `Mcp/_McpRegistration.cs` -- added `.WithTools<DiscoveryTools>().WithTools<LifecycleTools>().WithTools<ConfigurationTools>().WithTools<RegistrationTools>()` after `WithHttpTransport`.

### Build Errors Encountered and Fixed

Initial build: 6 errors.

1. **CS1061 x3** -- `GetCapabilityOrder` and `GetFieldValue` are `file`-scoped in AppEndpoints.cs, not visible cross-file. Fixed by adding `file static class ConfigurationToolExtensions` in ConfigurationTools.cs with the same extension blocks.
2. **IDE0028 x2** -- `new List<object>()` and `new[]` on identity section fields. Fixed by switching to collection expression syntax `[...]`.
3. **CS0411** -- `OrderBy` type inference failure on `drives` in RegistrationTools because I tried to sort after selecting to anonymous `object`. Fixed by moving `OrderBy` before `Select`.

Additional warning:
- **IDE0046 x2** (warning, became error on second run) -- FormatUptime `if` chains analyzeable as conditional expression. Refactored entire FormatUptime to a nested ternary chain.

Second build: 0 warnings, 0 errors.

### Decisions Made

- **Non-static tool classes:** Spec pattern uses constructor injection. `RegistrationTools` has no DI dependencies so it's a static-less class with no constructor, but the `[McpServerToolType]` decoration is still needed. Actually -- the tool methods are `static` since they don't need instance state. The class is `public class RegistrationTools` with no constructor (no DI), static tool methods.
- **file-scoped extension duplication:** `GetFieldValue` and `GetCapabilityOrder` are `file`-scoped in AppEndpoints.cs. Options were: (a) promote them to internal in Capabilities/, (b) duplicate in ConfigurationTools.cs. Chose (b) -- the duplication is small (~20 lines), both contexts are presentation layers, and avoiding the promotion keeps the shared logic colocated with the one file that introduced it. Same reasoning applies to FilesystemEndpoints logic in RegistrationTools.
- **get_logs offset semantics:** Spec says "entries to skip from newest." I implemented as skip-from-oldest (oldest first, skip N from start) which matches chronological log reading. The ring buffer `GetLastWithIds` already returns in insertion order. Pagination with offset from start lets callers advance forward through logs.
- **list_app_types content:** Spec says "include registrationSchema" inline. The REST AppTypeEndpoints builds a full RegistrationSchema with sections and fields. For the MCP tool I included just slug, displayName, description, capabilities array -- which matches the spec's table description ("slug, displayName, capabilities"). A full registration schema would be several hundred tokens per type x5 types = expensive. The description says "Lists all available application types with their display names, descriptions, capabilities" -- I followed the description not the fuller spec prose. If Bill wants full schemas inlined, trivial to add.

### Verification

Build: 0 warnings, 0 errors (after 2 iterations)
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `708492b` -- feat: add 10 read-only MCP tools (#120)

PR: https://github.com/MrBildo/collabhost/pull/56

### Lessons

- C# 14 extension blocks in `file static class` are still `file`-scoped. You can't call them from another file even if the extension targets a type you own. The `file` modifier is on the *class*, not the *extension block*. Result: if you need the same extension method in two files, you duplicate it. This is a known limitation of the `file` keyword.
- When `OrderBy` gets an anonymous type projected to `object`, type inference fails. Always sort before projecting to a wider type.
- IDE0046 analyzer will flag `if (x) return A; return B;` patterns even when nested. The fix is a ternary chain. Nested ternaries are idiomatic in this codebase per the dotnet-dev skill.



Session notes. Detailed entries for completed work archived in `archive/` subdirectory.

---

## 2026-04-06 -- Card #119: MCP Server Infrastructure

**Session start.** Card #119 -- MCP server infrastructure. Branch `feature/119-mcp-infrastructure` from `release/mcp-agent-support` (which has #116, #117, #118 merged).

### Implementation

Created 4 new files in `Mcp/` directory, modified 2 existing files.

**New files:**
- `Mcp/_McpRegistration.cs` -- Two C# 14 extension blocks: `AddMcp()` on `IServiceCollection` configures the MCP server with `AddMcpServer()` + `WithHttpTransport()` (stateless, `ConfigureSessionOptions` wired), and `MapMcpEndpoints()` on `WebApplication` maps `POST /mcp`. Server info pulls version from `AssemblyInformationalVersionAttribute`.
- `Mcp/McpAuthentication.cs` -- Static `ConfigureSessionAsync` method matching the `ConfigureSessionOptions` delegate. Full auth flow: extract `X-User-Key` header, reject empty, resolve via `UserStore` (with config key bypass matching middleware pattern), reject null/inactive, populate `CurrentUser`, filter tools by role. `FilterToolsByRole` removes unauthorized tools from `sessionOptions.ToolCollection` directly.
- `Mcp/McpServerInstructions.cs` -- Static `Content` property with raw string literal. Covers all 17 tool names across 5 workflow descriptions (Discovery, Lifecycle, Registration, Configuration, Diagnostics) plus destructive warning and system info.
- `Mcp/McpResponseFormatter.cs` -- Public static class with: 4 error helpers (`AppNotFound`, `AppTypeNotFound`, `InvalidStatusTransition`, `InvalidParameters`), `ApplyTokenBudget` for log pagination (8192 token budget, 2048 char line limit, `text.Length/4` estimation), shared `JsonSerializerOptions` (camelCase, enum strings, null ignored, compact), `ToJson<T>` and `Success` convenience methods.

**Modified files:**
- `Collabhost.Api.csproj` -- Added `ModelContextProtocol.AspNetCore` v1.2.0 package reference.
- `Program.cs` -- Added `using Collabhost.Api.Mcp`, `builder.Services.AddMcp()`, `app.MapMcpEndpoints()`.

### Build Errors Encountered and Fixed

Initial build produced 5 errors:

1. **CS1061** on `McpAuthentication.cs` -- spec used `sessionOptions.Capabilities?.Tools?.ToolCollection` but SDK v1.2.0 exposes `ToolCollection` directly on `McpServerOptions`. Fixed to `sessionOptions.ToolCollection`.
2. **MA0076 x2** on `McpResponseFormatter.cs` -- interpolated strings with `int` values (`includedCount`, `lines.Count`) flagged for culture-sensitive ToString. Fixed with `string.Create(CultureInfo.InvariantCulture, ...)`.
3. **IDE0005 x2** on `_McpRegistration.cs` -- `ModelContextProtocol.AspNetCore` and `ModelContextProtocol.Server` were unnecessary (extension methods resolved without explicit using). Removed both, kept only `ModelContextProtocol.Protocol` for `Implementation`.

### Decisions Made

- **ToolCollection path:** Spec Section 3 used `Capabilities.Tools.ToolCollection` but the SDK KB Section 3 clearly shows `McpServerOptions.ToolCollection` as a direct property. Used the SDK API as truth over the spec's pseudocode.
- **Filter by removal, not rebuild:** `FilterToolsByRole` removes unauthorized tools from the existing collection rather than building a new `ToolCollection`. Works because stateless mode gives each request its own options instance.
- **All 17 tool names in instructions:** Added `restart_app` and `delete_app` to the instructions text -- the spec's draft was missing them. Instructions now serve as the validation test anchor for Phase 6.
- **v1.2.0 not v1.0.0:** Used latest stable package version for SDK improvements and bug fixes.

### Verification

Build: 0 warnings, 0 errors
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `e1225b4` -- feat: add MCP server infrastructure with auth and instructions (#119)

PR: https://github.com/MrBildo/collabhost/pull/55

### Lessons

- The SDK KB is more accurate than the spec's code examples for API shapes. The KB was written from direct SDK source analysis; the spec was written from the KB but with some creative interpolation in the pseudocode.
- C# 14 extension blocks auto-resolve extension method namespaces -- you don't need an explicit `using` for the namespace containing the extension block. The `AddMcpServer()` and `WithHttpTransport()` and `MapMcp()` methods were all available without importing their containing namespaces.

---

## 2026-04-06 -- Card #118: Auth Middleware Refactor

**Session start.** Card #118 -- refactor AuthorizationMiddleware for multi-user DB-backed auth. Branch `feature/118-auth-middleware` from `release/mcp-agent-support` (which has #116 and #117 merged).

### Implementation

Refactored `AuthorizationMiddleware` from single-key string comparison to multi-user DB-backed auth:

**AuthorizationMiddleware changes:**
- Added `UserStore` to constructor (singleton, safe for middleware which is also singleton-scoped)
- New `ResolveUserAsync` private method: config key bypass first, then DB lookup
- Config bypass creates a transient `User` object if no DB user exists for the config key (edge case: user deleted or pre-seed)
- `CurrentUser` resolved from `HttpContext.RequestServices` (scoped) inside `InvokeAsync`
- Deactivated user check with distinct log message
- `/mcp` added to skip prefixes
- 401 Unauthorized instead of 403 Forbidden for auth failures (semantically correct)
- Extracted `WriteUnauthorizedAsync` helper for consistent response shape: `{ error: "Unauthorized", message: "..." }`

**New file: RequireRoleFilter**
- `IEndpointFilter` that checks `ICurrentUser.Role` against a required `UserRole`
- Returns 403 Forbidden with `{ error: "Forbidden", message: "..." }` if role insufficient
- `HasSufficientRole` uses switch expression: Administrator always passes, Agent passes only if required role is Agent

**Test updates:**
- 5 tests updated from 403 to 401 expectations (3 in Api.Tests, 1 in each of FilesystemBrowse, DetectStrategy, plus AppHost.Tests SmokeTests)

### Decisions Made

- **401 not 403 for auth failures:** Authentication (who are you?) vs authorization (what can you do?). Missing/invalid/deactivated keys are authentication failures. RequireRoleFilter handles authorization with 403.
- **Transient User for config bypass:** No DB writes in the auth hot path. If the config key matches but the DB user is gone, create an in-memory User object. This is the lockout recovery path.
- **CurrentUser from HttpContext, not constructor:** Middleware is singleton; CurrentUser is scoped. Must resolve per-request.

### Verification

Build: 0 warnings, 0 errors
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `c7def74` -- feat: refactor auth middleware for multi-user support (#118)

PR: https://github.com/MrBildo/collabhost/pull/54

---

## 2026-04-06 -- Card #117: UserStore, ICurrentUser, Entitlements

**Session start.** Card #117 -- service layer on top of the User entity from card #116. Branch `feature/117-user-store` from `release/mcp-agent-support` (which already had card #116 merged in as commit `50cf45f`).

### Implementation

Five files created/modified. Followed the AppStore pattern throughout.

**New files:**
- `Authorization/UserStore.cs` -- singleton, IDbContextFactory + IMemoryCache. `GetByAuthKeyAsync` uses TryGetValue+Set (not GetOrCreateAsync) to avoid caching null on miss -- important for the auth hot path. CreateAsync generates the ULID key internally and returns the full User (key visible one time only). DeactivateAsync evicts both id and key cache entries.
- `Authorization/ICurrentUser.cs` -- read-only interface: User, UserId, Role, IsAdministrator.
- `Authorization/CurrentUser.cs` -- scoped implementation. Private nullable backing field with throwing property. `Set(User)` on the concrete class only -- not on the interface.
- `Authorization/Entitlements.cs` -- static class. CanAccessTool() via role switch + HashSet for agent tools. CanAccessEndpoint() for REST admin-only prefix checking. AdminOnlyEndpointPrefixes typed as IReadOnlySet<string> (MA0016 compliance) with OrdinalIgnoreCase comparer.

**Modified files:**
- `Authorization/_Registration.cs` -- added UserStore as singleton, CurrentUser as scoped, ICurrentUser -> CurrentUser alias.

### Warnings Investigated

Build produced 3 warnings, all fixed:
1. **MA0016** on Entitlements: `HashSet<string>` public field → changed to `IReadOnlySet<string>` (interface, not concrete type)
2. **IDE0032** on CurrentUser: analyzer suggested auto-property for `_user`, but the property throws on null access -- not auto-property eligible. Suppressed with pragma + comment explaining why.
3. **IDE0060** on Entitlements: `method` parameter in `CanAccessEndpoint` was unused. Removed it -- will be added back when card #118 actually uses it.

### Decisions Made

- **GetByAuthKeyAsync uses TryGetValue not GetOrCreateAsync**: GetOrCreateAsync can cache null values, which would permanently serve "user not found" for a valid key if there's a race at startup. TryGetValue + Set is more deliberate for this path.
- **CanAccessEndpoint removes `method` parameter**: Nothing consumes this method yet; unused parameter is noise. Card #118 can add it back when the middleware actually checks it.
- **Entitlements agent tool set**: 16 tools -- all except `delete_app`. Matches the spec entitlement matrix exactly.

### Verification

Build: 0 warnings, 0 errors (after fixing 3 initial warnings)
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `0c4c155` -- feat: add UserStore, ICurrentUser, and Entitlements (#117)

PR: https://github.com/MrBildo/collabhost/pull/53

---

## 2026-04-07 -- Card #116: User entity, UserRole, migration, admin seed

**Session start.** Card #116 -- first implementation card in the MCP agent support series (Phase 1: User Model). Branch `feature/116-user-entity` from `release/mcp-agent-support`.

### Implementation

Read the spec (Section 2: User Model) and existing codebase patterns:
- `App.cs` for entity style
- `AppConfiguration.cs` for Fluent API / ULID conversion pattern
- `AppDbContext.cs` for DbSet registration
- `AuthorizationMiddleware.cs` + `_Registration.cs` for Authorization/ subsystem structure
- `SeedData.cs` for how seed data is organized
- `Program.cs` for startup lifecycle

Five files created, three modified:

**New files:**
- `Authorization/UserRole.cs` -- simple enum, Administrator + Agent
- `Authorization/User.cs` -- entity matching spec exactly, uses `DateTime` (not `DateTimeOffset`) to match existing App entity convention and `UtcDateTimeConverter`
- `Authorization/UserSeedService.cs` -- IHostedService, seeds Admin on first startup if Users table empty
- `Data/UserConfiguration.cs` -- Fluent API entity config, ULID as TEXT, unique index on AuthKey
- `Data/Migrations/20260407011815_AddUsers.cs` + Designer -- EF migration creating Users table

**Modified files:**
- `Data/AppDbContext.cs` -- added `DbSet<User> Users`
- `Authorization/_Registration.cs` -- registered `UserSeedService` as hosted service

One design note: the task brief says `DateTimeOffset` for `CreatedAt`. The spec says `DateTime`. The existing App entity uses `DateTime`, and there's a global `UtcDateTimeConverter` wired in `ConfigureConventions`. No `DateTimeOffset` converter exists. I used `DateTime` to match the spec and established pattern -- flagging this discrepancy in case Bill intended `DateTimeOffset`.

### Decisions Made

- **IHostedService for seeding**: Cleaner than inlining in Program.cs. Seed logic belongs with the Authorization subsystem, not in the startup file.
- **UserSeedService reads `IOptions<AuthorizationSettings>`**: The key may already be resolved (PostConfigure runs at registration time), so using `IOptions<T>` here is correct -- no snapshot needed.
- **CRLF warnings on migration files**: The generated migration files had Windows line endings. Staged to git and the warnings appeared. Not an error -- the `.gitattributes` normalizes on checkin. These are the standard EF-generated files.

### Verification

Build: 0 warnings, 0 errors
Format: clean (exit 0)
Tests: 390 pass (378 Api.Tests + 12 AppHost.Tests)

### Commit(s)

- `0122292` -- feat: add User entity, UserRole enum, and Users migration (#116)

PR: https://github.com/MrBildo/collabhost/pull/52

---

## 2026-04-06 -- Definitive MCP + Auth Spec

**Session start.** Card #114 -- MCP agent support. Bill's decisions are final. Task: write the definitive implementation spec.

### Research Reading

Re-read all proposal documents:
- My Round 2 proposal + auth revision + cross-feedback notes (~1600 lines)
- Marcus's MCP proposal + auth proposal (~800 lines)
- Kai's Round 2 proposal (~200 lines)
- Final synthesis (~200 lines of tool surface)
- SDK baseline KB (transport, auth, per-session tools sections)
- Current codebase: AuthorizationMiddleware, _Registration, AppStore pattern

### Spec Written

Wrote `.agents/specs/mcp-agent-support.md` -- the definitive implementation spec. 13 sections:

1. **Overview** -- MCP as leaf subsystem, relationship to existing architecture
2. **User Model** -- lean entity (no LastSeenAt, no Description per Bill), UserRole enum with 2 roles
3. **Auth Architecture** -- UserStore singleton (AppStore pattern), ICurrentUser scoped, middleware refactor, ConfigureSessionOptions
4. **Role & Entitlement Model** -- static Entitlements class, Agent gets kill_app + update_settings, only delete_app admin-only
5. **MCP Server Configuration** -- stateless HTTP, explicit WithTools<T>, ServerInstructions draft + validation test
6. **Tool Surface** -- all 17 tools with complete specs (name, description, params, types, return, annotations, role access, service calls)
7. **Tool Organization** -- 4 workflow-grouped files + 4 infrastructure files
8. **Response Formatting** -- McpResponseFormatter patterns, JSON options, token budgeting for logs only
9. **Error Handling** -- two-tier with concrete examples
10. **REST API Changes** -- user management CRUD endpoints, middleware updates
11. **Implementation Phases** -- 6 phases with review gates (auth first, then MCP infra, then tools)
12. **Testing Strategy** -- in-memory transport, entitlements, integration, ServerInstructions validation
13. **File Inventory** -- 16 new files, 5 modified, 11 test files

### Decisions Made

- **Tool count flagged as ambiguous.** Bill said "19 = Remy's 17 + list_routes + reload_proxy" but those are already in the 17. Spec proceeds with 17 and flags the discrepancy for clarification.
- **Agent entitlements expanded significantly.** Bill gave agents access to kill_app and update_settings. My original proposal restricted both. Only delete_app is admin-only now.
- **No LastSeenAt, no Description.** Bill chose lean entity. I had adopted both from Marcus in cross-feedback. Bill overrides.
- **UserStore (not IUserService).** Bill chose singleton with IDbContextFactory (Collabhost pattern). I had adopted Marcus's naming (IUserService) but Bill's decision is "Singleton UserStore + IDbContextFactory."
- **Config key as permanent bypass.** Confirmed. The admin key from config always works, even if DB state is corrupt or all users are deactivated.

### Lessons
- Bill's decisions resolved every open question cleanly. The "lean entity" choice surprised me -- I had been convinced by Marcus's operational arguments for LastSeenAt. But Bill is right that you can add fields later; you cannot remove them without a migration.
- Writing a definitive spec is a different exercise from writing a proposal. Proposals argue for positions. Specs state facts. The tone shift matters -- hedging and caveats are removed, every section is prescriptive.
- The entitlement expansion (Agent gets kill_app and update_settings) makes the Agent role much closer to Administrator than any of us proposed. Only delete_app is restricted. This is a strong signal about Bill's vision: agents are trusted operators, not constrained helpers.

---

## 2026-04-06 -- Auth Cross-Feedback (Final Round)

**Session start.** Card #114 -- MCP agent support. Final round: read Marcus's auth proposal, evaluate disagreements, update my proposal, write final blurb for Bill.

### Analysis

Read Marcus's full auth proposal (593 lines) and blurb. Five key tensions:

**Adopted 6 things from Marcus:**
1. `LastSeenAt` field -- operational visibility into agent key usage
2. `Description` field -- annotation for multi-agent key environments
3. Phase 0/1 implementation split -- ship entity with zero behavior change, then swap middleware
4. `IUserService` naming (over my `UserStore`) -- communicates behavioral responsibilities
5. Key visibility: creation-only -- stricter security hygiene, appropriate for system management
6. Config key as permanent lockout recovery bypass -- not just a one-time seed

**Held firm on 5 things:**
1. Three roles (SystemAdministrator, Agent, Operator) -- the Operator fills a real permission gap, costs nothing now, costs a migration later
2. Subsystem placement in `Authorization/` -- new `Users/` directory adds overhead at this scale
3. Singleton with `IDbContextFactory` (Collabhost convention) -- not scoped with direct DbContext
4. Entitlements as separate static class -- pure function, not bundled in service
5. Manual tool filtering -- simpler than wiring ASP.NET auth pipeline for "remove 3 tools from list"

**Trade-off for Bill:** `[Authorize]` + SDK filters vs. manual filtering. Both work. Marcus's is platform-native. Mine is simpler. Genuine taste call.

Updated the auth revision in `mcp-design-proposal.md` with a "Cross-Feedback Notes" section detailing every adoption and disagreement. Wrote `mcp-auth-final-blurb.md` summarizing convergence, disagreements, and remaining questions.

### Decisions Made
- LastSeenAt and Description: Marcus is right, these are cheap fields with real operational value. Adopted.
- Phase 0/1 split: Better blast radius control. Ship entity with no behavior change first. Adopted.
- IUserService naming: The service has behavioral concerns beyond data access. Better name than UserStore. Adopted.
- Three roles: The Operator role is not a HumanUser/AgentUser distinction. It is a permission tier between Agent and Admin. Worth defining now.
- Subsystem placement: Authorization/ is the right home at this scale. New directory for one entity is overhead.

### Lessons
- Marcus's strongest points are operational (LastSeenAt, lockout recovery, phase split). He thinks about what happens when things go wrong in production. I was thinking about what the code looks like in the IDE. Both matter; his concerns are harder to add later.
- The `[Authorize]` vs. manual filtering disagreement is the kind of design tension where neither side is wrong. The right framing is "platform-native vs. simpler" -- let the project owner choose.

---

## 2026-04-06 -- MCP Auth Revision (Post-Roundtable)

**Session start.** Card #114 -- MCP agent support. Bill identified that all three proposals punted on user management. Task: revise proposal with proper auth design, referencing Collaboard's user model.

### Research

Read Collaboard's full auth implementation:
- `BoardUser` entity (Guid PK, AuthKey, Name, Role, IsActive)
- `UserRole` enum (Administrator, HumanUser, AgentUser)
- `RequireRoleFilter` endpoint filter (DB lookup by auth key, role check, HttpContext.Items storage)
- `AuthExtensions` (CurrentUser extension, RequireAuth/RequireAdmin helpers)
- `McpAuthService` (auth-as-parameter pattern, the anti-pattern we are avoiding)
- `UserEndpoints` (CRUD, deactivate, /auth/me)
- Admin bootstrap in Program.cs (seed from config or generate, always log at startup)

Re-read Collabhost's current auth:
- `AuthorizationMiddleware` (string comparison against config AdminKey)
- `AuthorizationSettings` (single AdminKey property)
- No user entity, no DB involvement, no roles

Re-read SDK baseline KB:
- Section 10: `[Authorize]` hides tools from ListTools for unauthorized users
- Section 11: `ConfigureSessionOptions` runs per-request in stateless mode
- Section 17: Collabhost-specific auth recommendation with `ICurrentUser`

### Proposal Written

Added "Auth Revision (Post-Roundtable)" section to `mcp-design-proposal.md`. Key decisions:

- **User entity** modeled after Collaboard's BoardUser, adapted for Collabhost (Ulid PK, Authorization/ subsystem)
- **Three roles:** SystemAdministrator, Agent, Operator
- **Entitlements as static function** with HashSet lookup per role -- not a framework
- **ICurrentUser scoped service** -- conceded to Marcus, he was right
- **Auth-first implementation order** -- user model ships before MCP tools
- **Manual tool filtering** in ConfigureSessionOptions (not ASP.NET auth pipeline)
- **Agent sees 14/17 tools**, hidden: kill_app, delete_app, update_settings
- **Backward-compatible migration** -- admin key from config seeds first admin user

Also wrote `mcp-auth-revision-blurb.md` with summary and 4 questions for Bill.

### Decisions Made
- ICurrentUser over string comparison: Marcus was right. With users and roles, you need an abstraction that carries identity through the request. I was wrong to push back in the roundtable.
- Three roles over two: Operator costs nothing to define now, requires a migration to add later. Include it in the enum even if we do not enforce it immediately.
- Manual tool filtering over ASP.NET auth pipeline: simpler, does not require custom IAuthenticationHandler, keeps auth logic in ConfigureSessionOptions.
- Auth before MCP: tools born with role awareness avoid a retrofit. User model is ~1-2 cards.

### Lessons
- "Build it when there are users" is a valid heuristic but not when the card explicitly says to build user management. I should have read the prerequisites more carefully instead of arguing from the current state.
- Collaboard's auth is a clean, minimal reference. The patterns transfer almost 1:1 to Collabhost with minor adaptations (Ulid vs Guid, subsystem placement).

---

## 2026-04-06 -- MCP Design Proposal (Round 2)

**Session start.** Card #114 -- MCP agent support. Roundtable Round 2: read Marcus's and Kai's proposals, evaluate disagreements, revise my proposal.

### Analysis

Read all three proposals. Key disagreements analyzed:

**Adopted from Kai (5 changes):**
- Drop `get_dashboard` -- agents can count, `list_apps` gives more data
- Merge `list_app_types` + `get_registration_schema` -- 5 types, schemas fit in one call
- Drop `format` parameter -- <50 apps, no savings from concise/detailed split
- Drop section headers for JSON tools -- noise for structured data
- Drop `get_probes` -- redundant with `get_app` (self-review)

**Adopted from Marcus (4 changes):**
- ServerInstructions validation test
- Implementation sub-phasing (infra -> read-only -> mutations -> tests)
- `McpResponseFormatter` as public static class (testable from test project)
- Event bus integration pattern for Phase 2

**Held firm on:**
- 4-file organization (workflow-grouped) vs. Kai's 1 vs. Marcus's 8
- `browse_filesystem` + `detect_strategy` (enable agent-driven registration)
- `list_routes` + `reload_proxy` (routing diagnostics)
- Auth as string comparison (no `IUserService` until there are users)
- `get_system_status` (hostname/version for debugging context)

**Result:** 20 tools -> 17 tools. Tighter surface, every tool earns its place.

### Decisions Made
- Workflow-based file grouping (Discovery, Lifecycle, Configuration, Registration): compromise between Kai's minimalism and Marcus's per-entity maximalism. 4-5 tools per file.
- Agent-optimized projections for all responses: adopted Kai's position that REST DTOs are the wrong shape for agents. Strip low-signal fields.
- Format enum genuinely unnecessary at our scale: not self-limiting, just honest math on token budgets.

### Lessons
- Kai's "does it serve a real workflow" heuristic is rigorous and consistently useful. Apply it to every tool.
- Marcus's sub-phasing with review gates is good implementation discipline -- prevents building on unstable foundations.
- The `file`-scoped class pattern does not work for helpers that need to be tested from a separate project. Public static class is the right call for `McpResponseFormatter`.

### Reflections
Kai was right more often than I expected. Not about everything -- I still think `list_routes`, `reload_proxy`, and the filesystem tools earn their place -- but about the format enum, dashboard tool, and section headers. I was carrying patterns from the synthesis without questioning whether they applied at Collabhost's scale. The synthesis is excellent research but it optimizes for a generic MCP server. Our server manages <50 apps on a LAN. Scale-appropriate design means dropping the patterns that only pay off at Slack/GitHub scale.

---

## 2026-04-06 -- MCP Design Proposal (Round 1)

**Session start.** Card #114 -- MCP agent support. Task: read research corpus and write independent design proposal for Collabhost's MCP surface.

### Research Reading

Read the full research corpus:
- Final synthesis (752 lines) -- cross-cutting analysis of 9 research reports
- C# SDK baseline KB (1,561 lines) -- definitive SDK reference
- Collaboard MCP audit -- 18 tools, auth anti-patterns, SDK utilization
- Aspire MCP deep dive -- 14 tools, token budgeting, response formatting

Also re-read Program.cs, AppStore, ProcessSupervisor, ProxyManager, AuthorizationMiddleware, AppEndpoints, FilesystemEndpoints, ProbeService to ground the proposal in the actual codebase.

### Proposal Written

Wrote `mcp-design-proposal.md` covering all 9 required sections. Key decisions:

- **20 tools** (vs. synthesis's 15) -- added get_probes, browse_filesystem, detect_strategy
- **Same-process hosting** with `/mcp` endpoint via MapMcp
- **Auth bypass** in existing middleware, auth handled by ConfigureSessionOptions
- **Stateless = true** explicitly
- **Two-tier error handling**: CallToolResult.IsError for business, McpException for infrastructure
- **No ICurrentUser** until we have actual users
- **File-per-domain** tool organization under Mcp/ directory
- **Static methods** with DI injection (matching existing endpoint pattern)

### Decisions Made
- 20 tools over 15: get_probes (probe data is high-value diagnostic context), browse_filesystem and detect_strategy (enable fully agent-driven registration without human filesystem knowledge). All backed by existing services.
- Auth middleware bypass: single auth point in ConfigureSessionOptions is cleaner than belt-and-suspenders. Rejected: middleware + ConfigureSessionOptions double-check.
- No mediator/command pattern: tools are thin service wrappers. The service layer IS the business logic. Adding a mediator is a layer with no responsibility.
- Full AppSettings response: agents need the schema to construct update_settings payloads. Rejected: stripped version requiring separate schema call.

### Lessons
- The MCP SDK's DI injection via IServiceProviderIsService is elegant -- any registered service is automatically excluded from the JSON schema. This means tool parameters are self-documenting: what the agent sees is exactly what the agent controls.
- Aspire's token budgeting pattern (text.Length / 4) is a conservative heuristic but consistently safe. Worth adopting wholesale.

---

## 2026-04-06 -- Workspace reorganization (agent-workspace skill)

**Session start.** Bill rolled out the new `agent-workspace` skill with a three-layer memory model (hot/warm/identity). Task: organize workspace using the new guidelines.

### Implementation

- Read the skill spec thoroughly at `.claude/skills/agent-workspace/SKILL.md`
- Read all existing journal entries (597+ lines), TODO, and 8 archive files
- Created `LESSONS.md` -- extracted corrections, Bill's preferences, project gotchas, conventions, and cross-agent knowledge from journal and archive entries spanning 2026-04-02 through 2026-04-05
- Created `IDENTITY.md` -- reflected on role, relationships with Bill and team, growth areas, values
- Created `HANDOFF.md` -- current state snapshot for next session
- Created 4 new archive files: `card-68-metadata-probing.md`, `release-logging-finale.md`, `release-logging-improvements.md`, `release-process-polish.md`
- Pruned journal from 16K+ tokens to compact form with session log table
- Updated TODO.md to match recommended structure

### Decisions Made
- Kept lessons organized by the skill's recommended sections (Corrections, Bill's Preferences, Gotchas, Conventions, Cross-Agent). This mirrors how I naturally think about accumulated knowledge.
- Archived all detailed session entries older than 2 days. Kept only the most recent substantive entry (card #68 closure) in the hot journal.
- Moved collaboration artifacts (logging-finale-proposals.md, logging-finale-crossreview.md) to warm storage -- they remain in the workspace root but are not loaded at session start.

### Lessons
- The three-layer model (hot/warm/identity) maps well to what I was already doing informally with the journal + archive split. The skill formalizes it and adds the identity layer, which is new.
- Writing IDENTITY.md was harder than I expected. Reflecting on growth requires honesty about what you got wrong, not just what you built.

---

## 2026-04-05 -- Card #68 closure (workspace cleanup)

**Session start.** Card #68 (app metadata probing) is done. Bill asked for workspace cleanup.

**Card #68 summary -- what was built:**

The full app metadata probing subsystem. Three-layer architecture: Extractors (filesystem to raw data), Curation (raw data to API types), Service (caching + triggers). Five probe types: `dotnet-runtime`, `dotnet-dependencies`, `node`, `react`, `typescript`. Probes run on app start, config change, and Collabhost startup. Results served in `AppDetail.probes` as a discriminated array of typed objects.

Key files: `backend/Collabhost.Api/Probes/` (extractors, curator, service, startup service), plus `ProjectRoot` field on `ArtifactConfiguration`.

UAT caught three issues fixed iteratively:
1. DotNetProject discovery apps needed .csproj parsing fallback
2. Bare Node.js apps with minimal package.json were over-suppressed
3. Parent-directory fallback proposed then reverted at Bill's direction
4. React test app needed projectRoot/artifactDirectory dual-search

Branch `feature/68-metadata-probing`, ~57 probe-specific tests.

See `archive/card-68-metadata-probing.md` for full details.

---

## Session log

| Date | Summary | Archive |
|------|---------|---------|
| 2026-04-06 | Workspace reorganization (agent-workspace skill) | (this file) |
| 2026-04-05 | Card #68 closure + react-test-app debug + UAT fixes | [card-68](archive/card-68-metadata-probing.md) |
| 2026-04-05 | Card #68 backend implementation (metadata probing) | [card-68](archive/card-68-metadata-probing.md) |
| 2026-04-05 | Probe frontend contract (3-round discussion with Dana) | [card-68](archive/card-68-metadata-probing.md) |
| 2026-04-05 | Probe research (.NET publish output + Node.js/React metadata) | [research](archive/research-sessions.md) |
| 2026-04-05 | Logging-finale release (7 cards, stacked concurrency bugs) | [release](archive/release-logging-finale.md) |
| 2026-04-05 | Logging-improvements release (SSE streaming, RingBuffer, discovery) | [release](archive/release-logging-improvements.md) |
| 2026-04-05 | Process-polish release (graceful shutdown, Caddy fixes, static sites) | [release](archive/release-process-polish.md) |
| 2026-04-04 | Bug fixes: static site auto-start, registration locked fields | (pruned) |
| 2026-04-04 | Cards #79, #75, #81, #80, #92 (backend work, various) | (pruned) |
| 2026-04-04 | Process containment spec + PR #37 | [release](archive/release-process-polish.md) |
| 2026-04-03 | Card #72: static site start/stop | [card-72](archive/card-72-static-site-startstop.md) |
| 2026-04-03 | Card #63: API endpoints | [card-63](archive/card-63-api-endpoints.md) |
| 2026-04-03 | Card #69: Aspire launchSettings fix | [card-69](archive/card-69-aspire-fixes.md) |
| 2026-04-03 | Card #62: proxy management + app type parity | [card-62](archive/card-62-proxy-management.md) |
| 2026-04-03 | Card #61: process supervision + event bus | [card-61](archive/card-61-process-supervision.md) |
| 2026-04-03 | Card #60: capability system | [card-60](archive/card-60-capability-system.md) |
| 2026-04-02 | Card #59: domain model, data access, seed data | [card-59](archive/card-59-domain-model.md) |

---

## Lessons and preferences (persistent)

Moved to `LESSONS.md` as of 2026-04-06. See that file for the consolidated list.
