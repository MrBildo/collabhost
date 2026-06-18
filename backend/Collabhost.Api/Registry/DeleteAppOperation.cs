using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Stop-then-delete an app (code-structure-conventions §8/§9 -- a concrete operation in its owning
// subsystem). The body is intent only: load -> stop-if-running -> disable-route-if-routing-only ->
// delete -> cleanup -> invalidate-probe-cache -> record -> shape the outcome. It records the
// app.deleted event via the base RecordAsync(eventType, appId, appSlug, metadataJson, ct) overload
// after the row is deleted (the live entity is gone by emit time -- the exact case that overload
// exists for), so the leaf carries no hand-built ActivityEvent and stays §8-leaf-negative-clean.
//
// THE one sanctioned REST<->MCP parity-fix of the spine arc (Rule 9, disclosed loudly): the
// pre-migration REST DeleteAppAsync called probeService.InvalidateProbeCache(app.Id) (early cache
// hygiene, Card #337); the MCP delete_app NEVER did -- a confirmed drift. Unifying both surfaces onto
// this one operation fixes it by construction: the cache invalidation now runs once here, so MCP
// delete invalidates the probe cache exactly as REST always did. This is the deliberate behavior
// change PR 7 ships; everything else about delete is byte-preserving on both surfaces.
//
// Surface-specific concerns stay at the surface, never here (the single-surface-guard precedent):
// the MCP delete_app's admin-only double-check + AppNotFound shape stay at the MCP adapter (REST
// returns an empty 404), exactly as MCP auth stays at the MCP surface.
public sealed class DeleteAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProbeService probeService,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<DeleteAppCommand, DeleteAppOutcome>(currentUser, activityEventStore)
{
    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    protected override async Task<OperationResult<DeleteAppOutcome>> ExecuteCoreAsync
    (
        DeleteAppCommand command,
        CancellationToken ct
    )
    {
        var app = await _store.GetBySlugAsync(command.Slug, ct);

        if (app is null)
        {
            return OperationResult<DeleteAppOutcome>.NotFound($"App '{command.Slug}' not found.");
        }

        // Capture before delete -- app won't exist after store.DeleteAppAsync.
        var appId = app.Id.ToString(null, CultureInfo.InvariantCulture);
        var appSlug = app.Slug;
        var appDisplayName = app.DisplayName;
        var appTypeSlug = app.AppTypeSlug;

        // Stop if running (10s graceful timeout, force-kill fallback). Byte-preserved from both
        // pre-migration surfaces.
        var process = _supervisor.GetProcess(app.Id);

        if (process is not null && process.IsRunning)
        {
            try
            {
                using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                await _supervisor.StopAppAsync(app.Id, timeoutCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout expired -- force kill
                try
                {
                    await _supervisor.KillAppAsync(app.Id, CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // Already stopped
                }
            }
            catch (InvalidOperationException)
            {
                // Already stopped
            }
        }

        // Routing-only apps (static-site, external-route): explicitly disable the route before the
        // row is deleted. Without this, the route survives in Caddy until the next SyncRoutesAsync
        // pass re-derives the live set from AppStore.ListAsync -- a small window where Caddy still
        // routes to an app that no longer exists. Card #348 fix-along, byte-preserved.
        var hasProcess = _typeStore.HasBinding(appTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(appTypeSlug, "routing");

        if (!hasProcess && hasRouting)
        {
            _proxy.DisableRoute(appSlug);
            _proxy.RequestSync();
        }

        await _store.DeleteAppAsync(app.Id, ct);

        _supervisor.CleanupDeletedApp(app.Id, appSlug);

        // Early hygiene on delete -- release this app's cached probe entry immediately rather than
        // letting it squat in the cache until the next periodic prune tick. The cache is keyed by
        // ULID, so a recreated app with the same slug always gets a fresh entry regardless. Card #337
        // fix-along on REST -- and now MCP too: the pre-migration MCP delete_app never invalidated the
        // probe cache (a confirmed REST<->MCP drift). Unifying both surfaces here fixes it by
        // construction. THE one sanctioned behavior change of the #406 spine arc.
        _probeService.InvalidateProbeCache(app.Id);

        // app.deleted, stamped via the base recorder. The string-id overload (not the App overload)
        // because the entity is gone -- the captured id + slug + display-name metadata is the shape
        // the base built for delete.
        await RecordAsync
        (
            ActivityEventTypes.AppDeleted,
            appId,
            appSlug,
            JsonSerializer.Serialize(new { displayName = appDisplayName }),
            ct
        );

        return OperationResult<DeleteAppOutcome>.Success(new DeleteAppOutcome(appSlug, appDisplayName));
    }
}
