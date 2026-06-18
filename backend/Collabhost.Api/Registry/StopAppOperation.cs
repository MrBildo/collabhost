using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Proxy;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Stop an app (code-structure-conventions §8/§9 -- a concrete operation in its owning subsystem).
// The dual-branch twin of StartAppOperation: a routing-only app disables its Caddy route; a
// process-bearing app stops the supervised process. Both branches were duplicated across the REST
// endpoint and the MCP tool and hand-synced on Card #350 (the persist-flag write-through that lets
// a disabled route survive Collabhost restart) -- the REST<->MCP drift §8 deletes.
//
// The body is intent only: load -> branch -> act (route or process) -> persist-flag (routing-only)
// -> record -> shape the outcome. The supervisor's InvalidOperationException bubbles to the
// Operation<,> base -> Conflict; no hand-built ActivityEvent (the base RecordAsync helper); no
// surface-result construction. Unlike Start, Stop's routing-only branch has no runtime-config-file
// render -- so the only failure path is the supervisor's, hoisted to the base.
public sealed class StopAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<StopAppCommand, AppActionOutcome>(currentUser, activityEventStore)
{
    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    protected override async Task<OperationResult<AppActionOutcome>> ExecuteCoreAsync
    (
        StopAppCommand command,
        CancellationToken ct
    )
    {
        var app = await _store.GetBySlugAsync(command.Slug, ct);

        if (app is null)
        {
            return OperationResult<AppActionOutcome>.NotFound($"App '{command.Slug}' not found.");
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

        // Routing-only apps (e.g. static sites): disable route instead of stopping a process.
        if (!hasProcess && hasRouting)
        {
            _proxy.DisableRoute(app.Slug);
            _proxy.RequestSync();

            // Persist the operator-stop intent (Card #350) so the disabled route state survives
            // Collabhost restart -- ProxyManager.HydrateRouteStatesFromPersistenceAsync reads this
            // column on boot. Mirrors the process-bearing Stop path's write-through (via
            // ProcessSupervisor.StopAppAsync).
            await _store.SetStoppedByOperatorAsync(app.Id, app.Slug, true, ct);

            await RecordAsync(ActivityEventTypes.AppStopped, app, ct);

            return OperationResult<AppActionOutcome>.Success
            (
                new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, ProcessState.Stopped, hasProcess, hasRouting)
            );
        }

        var managed = await _supervisor.StopAppAsync(app.Id, ct);

        await RecordAsync(ActivityEventTypes.AppStopped, app, ct);

        return OperationResult<AppActionOutcome>.Success
        (
            new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, managed.State, hasProcess, hasRouting)
        );
    }
}
