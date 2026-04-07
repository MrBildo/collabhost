# Activity Log — Implementation Spec

> Card #103. All design decisions finalized by Bill and Nolan.
> Spec author: Remy, 2026-04-07.

---

## 1. Overview

The Activity Log is a new Collabhost subsystem that records every state-changing action with full actor identity. It provides an auditable history of who did what, when, and to which app. System-initiated events (auto-start, crash, seed) use a "system" actor.

**Scope:** Mutations only. No read operations logged.

---

## 2. Event Entity

### 2.1 ActivityEvent

New file: `ActivityLog/ActivityEvent.cs`

```
Namespace: Collabhost.Api.ActivityLog

public class ActivityEvent
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public required string EventType { get; init; }        // e.g. "app.started", "user.created"
    public required string ActorId { get; init; }           // ULID string, or "system"
    public required string ActorName { get; init; }         // Display name, or "System"
    public string? AppId { get; init; }                     // ULID string (null for non-app events)
    public string? AppSlug { get; init; }                   // For display/filtering (null for non-app events)
    public string? MetadataJson { get; init; }              // JSON blob, nullable
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
```

### 2.2 Design Decisions

**Metadata as JSON column.** The 16 event types have varied metadata shapes (exitCode for crashes, changedCapabilities for settings, targetUserId for user events). A JSON column avoids a wide sparse table or a separate metadata table per event type. The metadata is only consumed for display; it is never queried by individual fields.

**ActorId + ActorName denormalized.** We store both because: (a) the system actor has no DB record to join against; (b) user names can change; the log should reflect the name at the time of the event; (c) avoids a join on every event list query.

**AppId + AppSlug denormalized.** Same rationale. Apps can be deleted; the log should retain the slug for display. AppSlug is also the natural filter parameter for "show me events for this app."

**No foreign keys.** ActivityEvent does not FK to Users or Apps. Events must survive entity deletion (a deleted app's events should remain in the log). The denormalized strings are the record of truth.

---

## 3. ActivityEventStore

New file: `ActivityLog/ActivityEventStore.cs`

Singleton service using `IDbContextFactory<AppDbContext>`. No `IMemoryCache` -- unlike AppStore/UserStore which serve the same entities repeatedly with cache invalidation tied to mutations, ActivityEventStore is append-only with no updates. The query path (`LIMIT N ORDER BY Id DESC` on the primary key index) is sub-millisecond in SQLite. Caching would add complexity (cache key management, staleness window) for no measurable gain at this scale. If caching is needed later, it is trivial to add.

**Singleton lifetime note:** `ActivityEventStore` is registered as singleton. This is why it works in both singleton hosted services (ProcessSupervisor, ProxyManager) AND scoped MCP tools. Singletons can be injected into scoped services, but not the reverse. An implementing agent MUST NOT register this as scoped or transient -- doing so would create a captive dependency error in the singleton hosted services.

### 3.1 Write Methods

```
Task RecordAsync(ActivityEvent activityEvent, CancellationToken ct)
```

Single write method. Callers construct the `ActivityEvent` and pass it in.

### 3.2 Query Methods

```
Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int limit, CancellationToken ct)
```

Returns the most recent N events, ordered by `Id DESC`. Used by the dashboard events endpoint. No caching -- just queries the DB directly each time.

```
Task<ActivityEventPage> QueryAsync(ActivityEventQuery query, CancellationToken ct)
```

Filtered, paginated query. Returns a page with items and a cursor for keyset pagination.

### 3.3 Query/Response Types

New file: `ActivityLog/_Queries.cs`

```
public record ActivityEventQuery
(
    string? Category,           // "app", "user", "proxy" -- filters by EventType prefix
    string? AppSlug,            // exact match on AppSlug
    string? ActorId,            // exact match on ActorId (ULID or "system")
    string? EventType,          // exact match on EventType (e.g. "app.started")
    DateTime? Since,            // events after this timestamp
    DateTime? Until,            // events before this timestamp
    int Limit = 50,             // page size (max 200)
    string? Cursor              // ULID of last event from previous page (keyset pagination)
);

public record ActivityEventPage
(
    IReadOnlyList<ActivityEvent> Items,
    string? NextCursor,         // ULID of last item, or null if no more pages
    bool HasMore
);
```

**Keyset pagination over offset pagination.** ULIDs are time-ordered and unique, so the cursor is the ULID of the last event on the current page. The next page queries `WHERE Id < @Cursor ORDER BY Id DESC`. This avoids the O(N) skip cost of offset pagination as the log grows. The `ORDER BY Id DESC` is equivalent to ordering by timestamp (ULIDs encode time) but uses the primary key index directly.

**Ordering consistency:** Both `GetRecentAsync` and `QueryAsync` MUST order by `Id DESC`. ULID ordering is the single source of truth for event ordering across both endpoints. Never use `Timestamp DESC` -- it would produce inconsistent ordering between the dashboard feed and the full query endpoint for events that share the same millisecond timestamp but differ in ULID random suffix.

### 3.4 Severity Derivation

Severity is **not stored** -- it is derived from EventType at query time via a pure `DeriveSeverity` function:

```csharp
static string DeriveSeverity(string eventType) => eventType switch
{
    ActivityEventTypes.AppCrashed => "error",
    ActivityEventTypes.AppFatal => "error",
    ActivityEventTypes.AppKilled => "warning",
    _ => "info"
};
```

This uses the `ActivityEventTypes` constants for compile-time safety. If the severity rules change, all events retroactively reflect the new rules, which is the correct behavior for a UI display concern.

---

## 4. EF Core Migration

### 4.1 Entity Configuration

New file: `Data/ActivityEventConfiguration.cs`

```
public class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion(
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture))
            .HasMaxLength(26);

        builder.Property(e => e.EventType).HasMaxLength(50);
        builder.Property(e => e.ActorId).HasMaxLength(26);
        builder.Property(e => e.ActorName).HasMaxLength(200);
        builder.Property(e => e.AppId).HasMaxLength(26);
        builder.Property(e => e.AppSlug).HasMaxLength(100);
    }
}
```

### 4.2 DbSet

Add to `Data/AppDbContext.cs`:

```
public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
```

### 4.3 Indexes

```
builder.HasIndex(e => e.Timestamp);                           // GetRecentAsync, time-range queries
builder.HasIndex(e => e.AppSlug);                             // Filter by app
builder.HasIndex(e => e.EventType);                           // Filter by event type
builder.HasIndex(e => e.ActorId);                             // Filter by actor
```

Note: the primary key on ULID already provides efficient keyset pagination (ORDER BY Id DESC). The Timestamp index is redundant with ULID ordering for most queries, but is needed for explicit `Since`/`Until` time-range filtering. All four indexes are non-unique, non-clustered.

### 4.4 Migration

Generate via:
```powershell
cd backend
dotnet ef migrations add AddActivityEvents --project Collabhost.Api
```

---

## 5. Emission Points — Event Catalog

For each of the 16 events, this section specifies exactly where the emission call goes.

**Mandatory emission convention:** Every new mutation endpoint or MCP tool MUST emit an activity event. This is a convention that should be added to the KB under .NET conventions. If you add a new endpoint or MCP tool that changes state, it needs an activity event. No exceptions.

**Emission pattern:** At the call site (REST endpoint handler or MCP tool method), immediately after the successful mutation, construct an `ActivityEvent` and call `ActivityEventStore.RecordAsync`. For system events emitted from hosted services, inject `ActivityEventStore` into the service.

**Actor capture pattern:** For operator events, inject `ICurrentUser` at the handler and read `currentUser.UserId` / `currentUser.User.Name`. For system events, use constants: `ActorId = ActivityActor.SystemId`, `ActorName = ActivityActor.SystemName`.

**Metadata rule:** Only populate `MetadataJson` when the event carries context beyond what is already on the entity columns (AppId, AppSlug, ActorId, ActorName). For events where the only data would be `{ appId, appSlug }`, leave `MetadataJson` as null. This reduces serialization overhead, storage, and honestly represents what metadata is for: extra context.

### System Actor Constants

New file: `ActivityLog/_Constants.cs`

```
public static class ActivityActor
{
    public const string SystemId = "system";
    public const string SystemName = "System";
}
```

### Event Type Constants

Same file: `ActivityLog/_Constants.cs`

```
public static class ActivityEventTypes
{
    public const string AppStarted = "app.started";
    public const string AppStopped = "app.stopped";
    public const string AppRestarted = "app.restarted";
    public const string AppKilled = "app.killed";
    public const string AppCreated = "app.created";
    public const string AppDeleted = "app.deleted";
    public const string AppSettingsUpdated = "app.settings_updated";
    public const string AppCrashed = "app.crashed";
    public const string AppFatal = "app.fatal";
    public const string AppAutoStarted = "app.auto_started";
    public const string AppAutoRestarted = "app.auto_restarted";
    public const string AppSeeded = "app.seeded";
    public const string ProxyReloaded = "proxy.reloaded";
    public const string UserCreated = "user.created";
    public const string UserDeactivated = "user.deactivated";
    public const string UserSeeded = "user.seeded";
}
```

All emission code and the `DeriveSeverity` function MUST reference these constants, never raw strings. This gives compile-time discoverability without enum conversion overhead.

**Note on `_Constants.cs` file justification:** With both `ActivityActor` (2 constants) and `ActivityEventTypes` (16 constants), this file has sufficient substance to stand alone as an underscore-prefixed grouping file.

---

### Event 1: `app.started`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppStarted`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `StartAppAsync` (line 434) | After `supervisor.StartAppAsync` returns (process apps) or after `proxy.EnableRoute` + `proxy.RequestSync` (static sites), before building the response |
| MCP | `Mcp/LifecycleTools.cs` | `StartAppAsync` (line 40) | Same position relative to the supervisor/proxy calls |

**Metadata:** `MetadataJson = null` (appId and appSlug are already entity columns).

**ICurrentUser threading:** Add `ICurrentUser currentUser` parameter to both handler methods. See Section 6.

---

### Event 2: `app.stopped`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppStopped`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `StopAppAsync` (line 490) | After `supervisor.StopAppAsync` returns or after `proxy.DisableRoute` + `proxy.RequestSync` |
| MCP | `Mcp/LifecycleTools.cs` | `StopAppAsync` (line 98) | Same |

**Metadata:** `MetadataJson = null` (appId and appSlug are already entity columns).

---

### Event 3: `app.restarted`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppRestarted`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `RestartAppAsync` (line 540) | After `supervisor.RestartAppAsync` returns |
| MCP | `Mcp/LifecycleTools.cs` | `RestartAppAsync` (line 153) | Same |

**Metadata:** `MetadataJson = null` (appId and appSlug are already entity columns).

---

### Event 4: `app.killed`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppKilled`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `KillAppAsync` (line 574) | After `supervisor.KillAppAsync` returns |
| MCP | `Mcp/LifecycleTools.cs` | `KillAppAsync` (line 197) | Same |

**Metadata:** `MetadataJson = null` (appId and appSlug are already entity columns).

---

### Event 5: `app.created`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppCreated`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `CreateAppAsync` (line 660) | After `store.CreateAsync` and all `SaveOverrideAsync` calls, before building the Created response |
| MCP | `Mcp/RegistrationTools.cs` | `RegisterAppAsync` (line 42) | Same position |

**Metadata:** `{ "appTypeSlug": "...", "displayName": "..." }` (appId and appSlug are already entity columns; metadata carries only the extra context).

---

### Event 6: `app.deleted`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppDeleted`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `DeleteAppAsync` (line 815) | After `store.DeleteAppAsync` and `supervisor.CleanupDeletedApp`, before returning NoContent. Capture app details (slug, displayName) before the delete call. |
| MCP | `Mcp/RegistrationTools.cs` | `DeleteAppAsync` (line 211) | Same |

**Metadata:** `{ "displayName": "..." }` (appId and appSlug are already entity columns; metadata carries only the display name for post-deletion reference).

**Note on delete + stop:** Per Bill's decision, no suppression logic. If `supervisor.StopAppAsync` fires within the delete flow and an `app.stopped` event is emitted by a future subscriber on ProcessStateChangedEvent, both events appear. But today, stop within the delete flow does NOT emit an `app.stopped` activity event because we emit at the REST/MCP handler level, not from ProcessSupervisor internals. The delete handler calls `supervisor.StopAppAsync` directly (not through the start/stop endpoints), so no `app.stopped` activity event fires. Only `app.deleted` fires. This is the correct behavior.

---

### Event 7: `app.settings_updated`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.AppSettingsUpdated`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Registry/AppEndpoints.cs` | `SaveAppSettingsAsync` (line 304) | After all `SaveOverrideAsync` calls complete successfully, before returning the updated settings response |
| MCP | `Mcp/ConfigurationTools.cs` | `UpdateSettingsAsync` (line 175) | Same |

**Metadata:** `{ "changedCapabilities": ["process", "routing"] }` (appId and appSlug are already entity columns; metadata carries only the capability change list).

The changed capabilities list is already computed in both handlers (it's the set of keys in the request body that are being saved).

---

### Event 8: `proxy.reloaded`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.ProxyReloaded`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Proxy/ProxyEndpoints.cs` | `ReloadProxyAsync` (line 95) | After `proxy.RequestSync()` |
| MCP | `Mcp/ConfigurationTools.cs` | `ReloadProxy` (line 318) | Same |

**Metadata:** none (system-wide action, no app context).

**Note:** Automatic proxy syncs (triggered by ProcessStateChangedEvent via ProxyManager) are NOT logged. Only operator-triggered reloads are logged. This is per Bill's decision.

---

### Event 9: `user.created`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.UserCreated`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Authorization/UserEndpoints.cs` | `CreateUserAsync` (line 26) | After `store.CreateAsync` returns, before building the Created response |

No MCP equivalent for user management.

**Metadata:** `{ "targetUserId": "...", "targetName": "...", "role": "administrator" }`

---

### Event 10: `user.deactivated`

**Operator event. Actor = ICurrentUser. EventType = `ActivityEventTypes.UserDeactivated`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| REST | `Authorization/UserEndpoints.cs` | `DeactivateUserAsync` (line 113) | After `store.DeactivateAsync` succeeds (not in the catch block), before returning the Ok response |

**Metadata:** `{ "targetUserId": "...", "targetName": "..." }`

---

### Event 11: `app.crashed`

**System event. Actor = "system". EventType = `ActivityEventTypes.AppCrashed`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Supervisor/ProcessSupervisor.cs` | `OnRuntimeCrash` (line 611) | After `process.MarkCrashed(exitCode)` and `PublishStateChanged`, before restart policy evaluation (line 659-668 area). Only emit when the exit code is NOT in the success exit codes list (i.e., it's a real crash, not a clean exit). |

**Metadata:** `{ "exitCode": 1 }` (appId and appSlug are already entity columns).

**Threading note:** `OnRuntimeCrash` is called from a synchronous `Exited` event callback. `ActivityEventStore.RecordAsync` is async. Use the same sync-over-async pattern already established in this method (`.GetAwaiter().GetResult()`), wrapped in a try/catch so logging failure never blocks crash handling.

---

### Event 12: `app.fatal`

**System event. Actor = "system". EventType = `ActivityEventTypes.AppFatal`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Supervisor/ProcessSupervisor.cs` | `OnRuntimeCrash` (line 691-704) | After `process.MarkFatal()` and `PublishStateChanged`, when `HasMaxRestartsExceeded()` is true |
| Auto | `Supervisor/ProcessSupervisor.cs` | `OnStartupFailure` (line 543-557) | After `process.MarkFatal()` and `PublishStateChanged`, when `HasMaxStartupRetriesExceeded()` is true |

**Metadata:** `{ "failureCount": 10 }` (appId and appSlug are already entity columns).

Where `failureCount` is `process.ConsecutiveFailures` (runtime) or `process.StartupFailures` (startup).

---

### Event 13: `app.auto_started`

**System event. Actor = "system". EventType = `ActivityEventTypes.AppAutoStarted`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Supervisor/ProcessSupervisor.cs` | `StartAsync` (line 45-100) | After `StartAppInternalAsync` succeeds for each auto-start app, inside the foreach loop |
| Auto | `Proxy/ProxyManager.cs` | `EnableAutoStartRoutesAsync` (line 329-367) | After `EnableRoute(app.Slug)` for each routing-only auto-start app |

**Metadata:** `MetadataJson = null` (appId and appSlug are already entity columns).

---

### Event 14: `app.auto_restarted`

**System event. Actor = "system". EventType = `ActivityEventTypes.AppAutoRestarted`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Supervisor/ProcessSupervisor.cs` | `OnRuntimeCrash` fire-and-forget Task.Run (line 726-757) | After `StartAppInternalAsync` succeeds inside the Task.Run, when the auto-restart completes successfully |

**Metadata:** `{ "restartCount": 3, "exitCode": 1 }` (appId and appSlug are already entity columns; metadata carries only the restart-specific context).

**Threading note:** This runs inside a fire-and-forget `Task.Run`. `ActivityEventStore` is a singleton, so it is safe to call from this context. Use regular `await` (the Task.Run already provides an async context).

---

### Event 15: `user.seeded`

**System event. Actor = "system". EventType = `ActivityEventTypes.UserSeeded`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Authorization/UserSeedService.cs` | `StartAsync` (line 24-56) | After `db.SaveChangesAsync` (the admin user insert), before the key hint log |

**Metadata:** `{ "role": "administrator" }`

**Note:** The seed user's ID and key should NOT appear in the event metadata for security reasons. Only the role is recorded.

**Injection:** Add `ActivityEventStore` to `UserSeedService` constructor.

---

### Event 16: `app.seeded`

**System event. Actor = "system". EventType = `ActivityEventTypes.AppSeeded`.**

| Source | File | Method | Where to Emit |
|--------|------|--------|---------------|
| Auto | `Proxy/ProxyAppSeeder.cs` | `SeedAsync` (line 28-77) | After `_appStore.CreateAsync(proxyApp, ...)` and `CreateCapabilityOverridesAsync`, before the success log |

**Metadata:** `MetadataJson = null` (appSlug is already an entity column; the seeded app is always "proxy").

**Injection:** Add `ActivityEventStore` to `ProxyAppSeeder` constructor.

---

## 6. ICurrentUser Threading

`ICurrentUser` is populated by `AuthorizationMiddleware` (REST) and `McpAuthentication.ConfigureSessionAsync` (MCP) but is currently injected into zero mutation handlers. Every operator event emission point needs `ICurrentUser` added to its parameter list.

### REST Endpoints (Minimal API parameter injection)

Add `ICurrentUser currentUser` parameter to these handler method signatures:

| File | Method | Current Params (abbreviated) |
|------|--------|------------------------------|
| `Registry/AppEndpoints.cs` | `StartAppAsync` | `slug, store, supervisor, proxy, probeService, ct` |
| `Registry/AppEndpoints.cs` | `StopAppAsync` | `slug, store, supervisor, proxy, ct` |
| `Registry/AppEndpoints.cs` | `RestartAppAsync` | `slug, store, supervisor, ct` |
| `Registry/AppEndpoints.cs` | `KillAppAsync` | `slug, store, supervisor, ct` |
| `Registry/AppEndpoints.cs` | `CreateAppAsync` | `request, store, proxy, ct` |
| `Registry/AppEndpoints.cs` | `DeleteAppAsync` | `slug, store, supervisor, ct` |
| `Registry/AppEndpoints.cs` | `SaveAppSettingsAsync` | `slug, request, store, probeService, ct` |
| `Proxy/ProxyEndpoints.cs` | `ReloadProxyAsync` | `proxy` |
| `Authorization/UserEndpoints.cs` | `CreateUserAsync` | `request, store, ct` |
| `Authorization/UserEndpoints.cs` | `DeactivateUserAsync` | `id, store, ct` |

Minimal API DI injection handles `ICurrentUser` automatically -- just add the parameter.

### MCP Tool Classes (Constructor injection)

Add `ICurrentUser` to the primary constructor of these tool classes. MCP tools are scoped DI, so `ICurrentUser` is correctly scoped per-request:

| File | Class | Current Constructor Params |
|------|-------|---------------------------|
| `Mcp/LifecycleTools.cs` | `LifecycleTools` | `appStore, supervisor, proxy` |
| `Mcp/RegistrationTools.cs` | `RegistrationTools` | `appStore, supervisor, proxy` |
| `Mcp/ConfigurationTools.cs` | `ConfigurationTools` | `appStore, supervisor, proxy, proxySettings` |

Also add `ActivityEventStore` to each MCP tool class constructor (for emitting events).

### Hosted Services (Constructor injection for store only)

These emit system events only -- no ICurrentUser needed:

| File | Class | Add to Constructor |
|------|-------|--------------------|
| `Supervisor/ProcessSupervisor.cs` | `ProcessSupervisor` | `ActivityEventStore` |
| `Proxy/ProxyManager.cs` | `ProxyManager` | `ActivityEventStore` |
| `Authorization/UserSeedService.cs` | `UserSeedService` | `ActivityEventStore` |
| `Proxy/ProxyAppSeeder.cs` | `ProxyAppSeeder` | `ActivityEventStore` |

**Important:** `ProcessSupervisor` and `ProxyManager` are singletons. `ActivityEventStore` is also a singleton. No lifetime mismatch.

---

## 7. System Actor Pattern

Events from hosted services and background tasks have no HTTP context, therefore no `ICurrentUser`.

### Constants

```csharp
public static class ActivityActor
{
    public const string SystemId = "system";
    public const string SystemName = "System";
}
```

### Usage Pattern

```csharp
// In a hosted service or background task (crash event with extra metadata):
var activityEvent = new ActivityEvent
{
    EventType = ActivityEventTypes.AppCrashed,
    ActorId = ActivityActor.SystemId,
    ActorName = ActivityActor.SystemName,
    AppId = appId.ToString(),
    AppSlug = process.AppSlug,
    MetadataJson = JsonSerializer.Serialize(new { exitCode })
};

await _activityEventStore.RecordAsync(activityEvent, CancellationToken.None);

// For events with no extra metadata (e.g., auto_started):
var activityEvent = new ActivityEvent
{
    EventType = ActivityEventTypes.AppAutoStarted,
    ActorId = ActivityActor.SystemId,
    ActorName = ActivityActor.SystemName,
    AppId = appId.ToString(),
    AppSlug = process.AppSlug
    // MetadataJson omitted -- appId/appSlug are already entity columns
};
```

### Sync-over-Async in Exited Callbacks

`ProcessSupervisor.OnRuntimeCrash` and `OnStartupFailure` are synchronous methods called from the `Process.Exited` event. They already use `.GetAwaiter().GetResult()` for store operations. The activity event emission should follow the same pattern, wrapped in try/catch:

```csharp
try
{
    _activityEventStore.RecordAsync(activityEvent, CancellationToken.None)
        .GetAwaiter().GetResult();
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", process.DisplayName);
}
```

Activity logging must never interfere with the critical path (crash handling, restart logic).

---

## 8. REST Endpoint

### 8.1 Dashboard Events Endpoint (Existing Frontend Contract)

The frontend already calls `GET /api/v1/dashboard/events?limit=N` and expects:

```typescript
type DashboardEventsResponse = {
  events: DashboardEvent[]
}

type DashboardEvent = {
  timestamp: string
  message: string
  appName: string | null
  source: string
  severity: 'info' | 'warning' | 'error'
}
```

The `getDashboardEvents` function in `frontend/src/api/endpoints.ts` (line 87) calls this endpoint. The `useDashboardEvents` hook (line 14 of `use-dashboard.ts`) polls it. The `EventList` component renders it. None of these are consumed in any page yet, but the pipeline is fully wired.

**Implementation:** Add the `/dashboard/events` route to `DashboardEndpoints.Map`. Query `ActivityEventStore.GetRecentAsync(limit)` and map to the frontend contract:

```csharp
private static async Task<IResult> GetEventsAsync
(
    int? limit,
    ActivityEventStore activityEventStore,
    CancellationToken ct
)
{
    var events = await activityEventStore.GetRecentAsync(
        Math.Min(limit ?? 20, 100), ct);

    var items = events.Select(e => new DashboardEventResponse(
        e.Timestamp,
        FormatEventMessage(e),
        e.AppSlug,
        e.ActorName,
        DeriveSeverity(e.EventType)
    ));

    return TypedResults.Ok(new { events = items });
}
```

**Message formatting:** The `message` field is a human-readable summary derived from EventType and metadata. Examples:
- `app.started` -> "started"
- `app.crashed` -> "crashed (exit code 1)"
- `app.settings_updated` -> "settings updated (process, routing)"
- `user.created` -> "created user DevBot (agent)"
- `proxy.reloaded` -> "proxy config reloaded"

The `source` field maps to `ActorName` (e.g., "Admin", "System", "DevBot").

### 8.2 Full Events API Endpoint

New endpoint for full filtered access:

```
GET /api/v1/events?category=app&appSlug=my-api&actorId=...&eventType=app.started&since=...&until=...&limit=50&cursor=...
```

### 8.3 Response DTOs

New file: `ActivityLog/ActivityLogEndpoints.cs` (DTOs co-located with the endpoint as `file`-scoped types, or as public records if needed by tests).

```csharp
public record ActivityEventItem
(
    string Id,
    string EventType,
    string ActorId,
    string ActorName,
    string? AppId,
    string? AppSlug,
    JsonElement? Metadata,
    DateTime Timestamp,
    string Severity
);

public record ActivityEventListResponse
(
    IReadOnlyList<ActivityEventItem> Items,
    string? NextCursor,
    bool HasMore
);
```

**Example response:**

```json
{
  "items": [
    {
      "id": "01JRZX...",
      "eventType": "app.started",
      "actorId": "01JRZ...",
      "actorName": "Admin",
      "appId": "01JRZ...",
      "appSlug": "my-api",
      "metadata": null,
      "timestamp": "2026-04-07T12:00:00Z",
      "severity": "info"
    }
  ],
  "nextCursor": "01JRZX...",
  "hasMore": true
}
```

**Severity** is derived via `DeriveSeverity`, not stored. **Metadata** is deserialized from the JSON column into a `JsonElement?` for the response (null when `MetadataJson` is null).

**Route group:** New file `ActivityLog/ActivityLogEndpoints.cs` with its own group under `/api/v1/events`. The activity log is its own subsystem.

```
group.MapGet("/", QueryEventsAsync);
```

### 8.4 Endpoint Registration

New file: `ActivityLog/_Registration.cs`

```csharp
public static class ActivityLogRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddActivityLog()
        {
            services.AddSingleton<ActivityEventStore>();
            return services;
        }
    }

    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapActivityLogEndpoints()
        {
            ActivityLogEndpoints.Map(routes);
            return routes;
        }
    }
}
```

Add to `Program.cs`:
- Service: `builder.Services.AddActivityLog();`
- Endpoint: `app.MapActivityLogEndpoints();`
- Dashboard events: add the `/events` route to `DashboardEndpoints.Map`

---

## 9. MCP Tool

### 9.1 Tool Definition

New tool in a new class: `Mcp/ActivityLogTools.cs`

```
[McpServerToolType]
public class ActivityLogTools(ActivityEventStore activityEventStore)
```

Register in `_McpRegistration.cs`: `.WithTools<ActivityLogTools>()`

### 9.2 `list_events` Tool

```
[McpServerTool(
    Name = "list_events",
    ReadOnly = true,
    Destructive = false,
    Idempotent = true,
    OpenWorld = false
)]
[Description("Lists recent activity events (state changes, operator actions, system events). Use to understand what happened to an app or who performed an action. Filter by app slug, event type, or category. Returns newest events first.")]
public async Task<CallToolResult> ListEventsAsync(
    [Description("Filter by app slug (e.g., 'my-api'). Only returns events for this app.")] string? appSlug,
    [Description("Filter by event type (e.g., 'app.crashed', 'app.started'). Exact match.")] string? eventType,
    [Description("Filter by category: 'app', 'user', or 'proxy'. Matches the prefix of event types.")] string? category,
    [Description("Maximum number of events to return (default 20, max 50).")] int? limit,
    CancellationToken ct
)
```

### 9.3 Token Budget

MCP tool responses should be concise. The tool should:
- Default to 20 events, max 50 per call
- Format each event as a single compact line: `[2026-04-07 12:00:00] app.started my-api by Admin`
- Include metadata only when it adds value (exit codes for crashes, changed capabilities for settings)
- Include a summary header: "Showing 20 of 150 events matching filter."

**Intentional limit divergence:** The MCP tool caps at 50 events per call, while the REST full endpoint (`GET /api/v1/events`) caps at 200. This is deliberate -- MCP responses are consumed as LLM context tokens, where every line counts. The REST endpoint serves UIs that can render large lists efficiently. Do not "fix" this asymmetry.

### 9.4 Response Format

```
Showing 20 events (newest first). Use appSlug, eventType, or category params to filter.

[2026-04-07 12:05:00] app.started my-api (by Admin)
[2026-04-07 12:04:30] app.stopped my-api (by Admin)
[2026-04-07 12:03:00] app.crashed my-api exit=1 (system)
[2026-04-07 12:00:00] app.auto_started proxy (system)
[2026-04-07 11:59:00] user.seeded role=administrator (system)
[2026-04-07 11:59:00] app.seeded proxy (system)
```

---

## 10. Phased Implementation Plan

### Phase 1: Foundation (Size: M)

**Goal:** Entity, store, migration, registration. The subsystem exists and can persist events.

**Files to create:**
- `ActivityLog/ActivityEvent.cs` -- entity
- `ActivityLog/ActivityEventStore.cs` -- store
- `ActivityLog/_Queries.cs` -- query/response types
- `ActivityLog/_Constants.cs` -- system actor constants + event type constants
- `ActivityLog/_Registration.cs` -- DI registration
- `Data/ActivityEventConfiguration.cs` -- EF Core config

**Files to modify:**
- `Data/AppDbContext.cs` -- add DbSet
- `Program.cs` -- add `builder.Services.AddActivityLog()`

**Deliverable:** Migration generated, store can write and read events. No emission points yet. Unit tests for store methods.

**Dependencies:** None.

### Phase 2a: REST Endpoint Emission (Size: M)

**Goal:** All 10 operator events emitting from REST endpoint handlers with full actor identity.

**Files to modify:**
- `Registry/AppEndpoints.cs` -- add ICurrentUser + ActivityEventStore to 7 handlers, emit events
- `Proxy/ProxyEndpoints.cs` -- add ICurrentUser + ActivityEventStore to ReloadProxyAsync
- `Authorization/UserEndpoints.cs` -- add ICurrentUser + ActivityEventStore to 2 handlers

**Deliverable:** Every REST mutation records an activity event. Tests verify events are written.

**Dependencies:** Phase 1.

### Phase 2b: MCP Tool Emission (Size: S)

**Goal:** All 10 operator events emitting from MCP tool methods with full actor identity.

**Files to modify:**
- `Mcp/LifecycleTools.cs` -- add ICurrentUser + ActivityEventStore to constructor, emit from 4 tools
- `Mcp/RegistrationTools.cs` -- add ICurrentUser + ActivityEventStore to constructor, emit from 2 tools
- `Mcp/ConfigurationTools.cs` -- add ICurrentUser + ActivityEventStore to constructor, emit from 2 tools

**Deliverable:** Every MCP mutation records an activity event. Tests verify events are written.

**Dependencies:** Phase 1.

**Note:** Phases 2a and 2b can run in parallel (REST endpoints vs MCP tools, no file overlap).

### Phase 3: System Event Emission (Size: M)

**Goal:** All 6 system events emitting from hosted services and background tasks.

**Files to modify:**
- `Supervisor/ProcessSupervisor.cs` -- add ActivityEventStore, emit app.crashed, app.fatal, app.auto_started, app.auto_restarted
- `Proxy/ProxyManager.cs` -- add ActivityEventStore, emit app.auto_started (routing-only)
- `Authorization/UserSeedService.cs` -- add ActivityEventStore, emit user.seeded
- `Proxy/ProxyAppSeeder.cs` -- add ActivityEventStore, emit app.seeded

**Deliverable:** System events recorded. Integration tests verify crash/auto-start/seed scenarios emit events.

**Dependencies:** Phase 1.

**Note:** Phases 2a, 2b, and 3 can all run in parallel (different files, independent concerns).

### Phase 4: REST Endpoints (Size: S)

**Goal:** Both event endpoints serving data, with named response DTOs.

**Files to create:**
- `ActivityLog/ActivityLogEndpoints.cs` -- full query endpoint + response DTOs (`ActivityEventItem`, `ActivityEventListResponse`)

**Files to modify:**
- `Dashboard/DashboardEndpoints.cs` -- add `/events` route for dashboard feed + `DashboardEventResponse` record
- `Program.cs` -- add `app.MapActivityLogEndpoints()`

**Deliverable:** Frontend can fetch events, full API queryable with filters and pagination. `FormatEventMessage` and `DeriveSeverity` implemented as pure static functions (prime unit test candidates).

**Dependencies:** Phase 1.

### Phase 5: MCP Tool (Size: S)

**Goal:** `list_events` MCP tool for agent access.

**Files to create:**
- `Mcp/ActivityLogTools.cs` -- tool class

**Files to modify:**
- `Mcp/_McpRegistration.cs` -- add `.WithTools<ActivityLogTools>()`

**Deliverable:** MCP agents can query activity history.

**Dependencies:** Phase 1.

**Note:** Phases 4 and 5 can run in parallel.

---

## 11. Frontend Brief (for Dana)

### What's New

1. **`GET /api/v1/dashboard/events?limit=N`** -- now returns real data instead of 404. Response matches the existing `DashboardEventsResponse` type exactly:

```typescript
// No changes to types.ts needed -- these types already exist:
type DashboardEventsResponse = {
  events: DashboardEvent[]
}

type DashboardEvent = {
  timestamp: string    // ISO 8601 UTC
  message: string      // Human-readable, e.g. "started", "crashed (exit code 1)"
  appName: string | null  // App slug (null for non-app events like proxy.reloaded)
  source: string       // Actor name: "Admin", "System", "DevBot"
  severity: 'info' | 'warning' | 'error'
}
```

2. **`GET /api/v1/events?...`** -- new full query endpoint. This is NOT needed for the dashboard EventList -- it's for future dedicated activity log page or advanced filtering.

### What Dana Needs to Do

- **Wire up the EventList on the Dashboard page.** `useDashboardEvents` hook and `EventList` component already exist and are tested. They just need to be rendered on the Dashboard page.
- **The EventList component uses array index keys** with a biome-ignore comment ("events lack unique IDs"). The backend response does not include IDs in the dashboard event shape (by design -- the dashboard feed is a simplified projection). The array index key is correct for this use case because the list re-fetches entirely on each poll; it never reconciles individual items.
- **No new types needed** in `types.ts` for the dashboard integration.
- **Future (out of scope for this card):** A dedicated "Activity Log" page with filtering, pagination, and the full `GET /api/v1/events` endpoint. That would need new types for the full event shape.

### `appName` vs `appSlug` mismatch -- for Dana to resolve

The frontend `DashboardEvent` type has a field called `appName`, but the backend maps `e.AppSlug` to this field. The backend activity event entity stores `AppSlug` (the url-safe identifier, e.g. `"my-api"`) and does NOT store a display name.

**Question for Dana:** Is `appName` intentionally the slug (which is what the frontend currently displays), or should it be the human-readable `DisplayName` (e.g., `"My API Server"`)? Options:

1. **Keep as slug.** The field name `appName` is slightly misleading but functional. No backend changes needed.
2. **Switch to DisplayName.** The `ActivityEvent` entity would need an `AppDisplayName` column added alongside `AppSlug`, and the backend would map `e.AppDisplayName` to the `appName` response field.

This is a pre-existing mismatch in the frontend types (predates the activity log), but this is the first time the backend will actually populate the field. The decision should be made before Phase 4 implementation.

### Severity Mapping

| Severity | Event Types |
|----------|-------------|
| `error` | `app.crashed`, `app.fatal` |
| `warning` | `app.killed` |
| `info` | Everything else |

---

## 12. File Tree Summary

New files:
```
backend/Collabhost.Api/
  ActivityLog/
    ActivityEvent.cs
    ActivityEventStore.cs
    ActivityLogEndpoints.cs
    _Constants.cs
    _Queries.cs
    _Registration.cs
  Data/
    ActivityEventConfiguration.cs
  Mcp/
    ActivityLogTools.cs
```

Modified files:
```
backend/Collabhost.Api/
  Data/AppDbContext.cs                      (add DbSet)
  Program.cs                                (add service + endpoints)
  Registry/AppEndpoints.cs                  (7 handlers: add ICurrentUser + emit)
  Proxy/ProxyEndpoints.cs                   (1 handler: add ICurrentUser + emit)
  Authorization/UserEndpoints.cs            (2 handlers: add ICurrentUser + emit)
  Mcp/LifecycleTools.cs                     (add ICurrentUser + ActivityEventStore)
  Mcp/RegistrationTools.cs                  (add ICurrentUser + ActivityEventStore)
  Mcp/ConfigurationTools.cs                 (add ICurrentUser + ActivityEventStore)
  Mcp/_McpRegistration.cs                   (add .WithTools<ActivityLogTools>())
  Supervisor/ProcessSupervisor.cs           (add ActivityEventStore, 4 emission points)
  Proxy/ProxyManager.cs                     (add ActivityEventStore, 1 emission point)
  Authorization/UserSeedService.cs          (add ActivityEventStore, 1 emission point)
  Proxy/ProxyAppSeeder.cs                   (add ActivityEventStore, 1 emission point)
  Dashboard/DashboardEndpoints.cs           (add /events route)
```

---

## 13. Open Questions

1. **`appName` vs `appSlug` in frontend `DashboardEvent` type.** See Section 11. Dana needs to decide whether the `appName` field should remain the slug or become the display name. If display name, the `ActivityEvent` entity needs an `AppDisplayName` column. This must be resolved before Phase 4 implementation.

All other design decisions have been made. This spec is ready for implementation.

-- Remy
