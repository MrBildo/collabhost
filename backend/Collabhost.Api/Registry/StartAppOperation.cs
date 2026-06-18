using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

// Start an app (code-structure-conventions §8/§9 -- a concrete operation in its owning subsystem).
// Unlike the process-only restart/kill (PR 2), start is DUAL-BRANCH: a routing-only app (a
// static-site or external-route -- has routing, no process) enables its Caddy route instead of
// spawning a process; a process-bearing app starts the supervised process. Both branches were
// near-line-for-line duplicated across the REST endpoint and the MCP tool and hand-synced across
// cards #350 (persist-flag), #365 (runtime-config-file writer), and #366 (probe refresh) -- the
// exact REST<->MCP drift §8 deletes by giving both surfaces one operation.
//
// The body is intent only: load -> branch -> act (route or process) -> probe -> persist-flag ->
// record -> shape the outcome. The supervisor's InvalidOperationException on a bad state transition
// bubbles to the Operation<,> base -> Conflict (the hoisted try/catch); no hand-built ActivityEvent
// (the base RecordAsync helper stamps the actor); no surface-result construction (each surface maps
// AppActionOutcome to its own shape). The one operation-specific failure the leaf translates itself
// is the runtime-config-file writer's RuntimeConfigFileWriteException -- a Start-routing-only-branch
// concern only this operation has, so it stays in the leaf (not hoisted into the base, which would
// burden every other operation) and maps to Conflict, exactly the 409/InvalidParameters both
// surfaces returned before. That is the leaf's own outcome decision, not generic try/catch-to-result
// plumbing: it returns a surface-agnostic OperationResult, never a surface result.
public sealed class StartAppOperation
(
    AppStore store,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProbeService probeService,
    RuntimeConfigFileWriter runtimeConfigFileWriter,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<StartAppCommand, AppActionOutcome>(currentUser, activityEventStore)
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

    private readonly RuntimeConfigFileWriter _runtimeConfigFileWriter = runtimeConfigFileWriter
        ?? throw new ArgumentNullException(nameof(runtimeConfigFileWriter));

    protected override async Task<OperationResult<AppActionOutcome>> ExecuteCoreAsync
    (
        StartAppCommand command,
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

        // Routing-only apps (e.g. static sites): enable route instead of starting a process.
        if (!hasProcess && hasRouting)
        {
            // Render runtime-config-file BEFORE enabling the route (Card #336). Ordering matters:
            // if we enabled the route first, Caddy could serve a stale on-disk value for an
            // arbitrary window before the writer landed the new file -- the cutover bug (#236) #336
            // set out to fix. The writer no-ops when the resolved Values is empty (preserves any
            // operator-maintained file on disk -- CLAUDE.md Rule 3). A write FAILURE is the one
            // operation-specific failure this leaf translates: it maps to Conflict, the
            // 409/InvalidParameters both surfaces returned before.
            var renderError = await RenderRuntimeConfigFileAsync(app, ct);

            if (renderError is not null)
            {
                return OperationResult<AppActionOutcome>.Conflict(renderError);
            }

            _proxy.EnableRoute(app.Slug);
            _proxy.RequestSync();

            // Clear the persisted operator-stop flag (Card #350) so the restored route state
            // survives Collabhost restart. Mirrors the process-bearing branch's write-through.
            await _store.SetStoppedByOperatorAsync(app.Id, app.Slug, false, ct);

            // Refresh probe-derived metadata so get_app reflects the current artifact state
            // (Card #366 -- the REST path called probes on both branches; the MCP path on neither).
            await _probeService.RunProbesAsync(app.Id, ct);

            await RecordAsync(ActivityEventTypes.AppStarted, app, ct);

            return OperationResult<AppActionOutcome>.Success
            (
                new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, ProcessState.Running, hasProcess, hasRouting)
            );
        }

        var managed = await _supervisor.StartAppAsync(app.Id, ct);

        await _probeService.RunProbesAsync(app.Id, ct);

        await RecordAsync(ActivityEventTypes.AppStarted, app, ct);

        return OperationResult<AppActionOutcome>.Success
        (
            new AppActionOutcome(app.Id, app.Slug, app.AppTypeSlug, managed.State, hasProcess, hasRouting)
        );
    }

    // The runtime-config-file render, returning the operator-actionable error message on failure
    // (or null on success). RuntimeConfigFileWriteException is specific to Start's routing-only
    // branch -- the leaf owns its translation to a normalized OperationResult.Conflict above. This
    // is the leaf's own outcome decision, kept out of the base (which would force the same catch on
    // every other operation that never throws it).
    private async Task<string?> RenderRuntimeConfigFileAsync(App app, CancellationToken ct)
    {
        try
        {
            await _runtimeConfigFileWriter.RenderAsync(app, ct);

            return null;
        }
        catch (RuntimeConfigFileWriteException exception)
        {
            return exception.Message;
        }
    }
}
