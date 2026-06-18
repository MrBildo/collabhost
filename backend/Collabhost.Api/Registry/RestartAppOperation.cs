using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Restart a process-based app (code-structure-conventions §8/§9 -- a concrete operation in its
// owning subsystem). The body is intent only: load -> act -> record -> shape the outcome. The
// supervisor's InvalidOperationException on a bad state transition (or a non-process app) bubbles
// to the Operation<,> base, which maps it to OperationResult.Conflict -- so this body carries no
// try/catch, no hand-built ActivityEvent (the base RecordAsync helper stamps the actor), and no
// surface-result construction (the surface maps AppActionOutcome to its own shape).
//
// Surface-specific concerns stay at the surface, never here: the MCP tool's "only process-based
// apps support restart" pre-check is an MCP-shaped Validation guard the MCP adapter keeps above
// this call (REST has no such guard -- it lets the supervisor throw -> 409), exactly as MCP auth
// stays at the MCP surface. This operation runs that-guard-agnostic, mirroring the REST body.
public sealed class RestartAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<RestartAppCommand, AppActionOutcome>(currentUser, activityEventStore)
{
    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    protected override async Task<OperationResult<AppActionOutcome>> ExecuteCoreAsync
    (
        RestartAppCommand command,
        CancellationToken ct
    )
    {
        var app = await _store.GetBySlugAsync(command.Slug, ct);

        if (app is null)
        {
            return OperationResult<AppActionOutcome>.NotFound($"App '{command.Slug}' not found.");
        }

        var managed = await _supervisor.RestartAppAsync(app.Id, ct);

        await RecordAsync(ActivityEventTypes.AppRestarted, app, ct);

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

        return OperationResult<AppActionOutcome>.Success
        (
            new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, managed.State, hasProcess, hasRouting)
        );
    }
}
