using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Force-kill a process-based app (code-structure-conventions §8/§9 -- a concrete operation in its
// owning subsystem). The body is intent only: load -> act -> record -> read post-kill state ->
// shape the outcome. The supervisor's InvalidOperationException ("No managed process found for this
// app." -- the not-running / non-process case) bubbles to the Operation<,> base, which maps it to
// OperationResult.Conflict; the body carries no try/catch, no hand-built ActivityEvent, and no
// surface-result construction.
//
// As with restart, the MCP tool's "only process-based apps support kill" pre-check is an
// MCP-surface Validation guard the MCP adapter keeps above this call (REST has none -- the
// supervisor throws -> 409); this operation mirrors the guard-free REST body. The post-kill state
// is read back from the supervisor (the kill may leave the process tracked in a terminal state, or
// removed entirely -> Stopped), exactly as both surfaces did before.
public sealed class KillAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<KillAppCommand, AppActionOutcome>(currentUser, activityEventStore)
{
    private readonly AppStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    protected override async Task<OperationResult<AppActionOutcome>> ExecuteCoreAsync
    (
        KillAppCommand command,
        CancellationToken ct
    )
    {
        var app = await _store.GetBySlugAsync(command.Slug, ct);

        if (app is null)
        {
            return OperationResult<AppActionOutcome>.NotFound($"App '{command.Slug}' not found.");
        }

        await _supervisor.KillAppAsync(app.Id, ct);

        await RecordAsync(ActivityEventTypes.AppKilled, app, ct);

        var process = _supervisor.GetProcess(app.Id);
        var state = process?.State ?? ProcessState.Stopped;

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

        return OperationResult<AppActionOutcome>.Success
        (
            new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, state, hasProcess, hasRouting)
        );
    }
}
