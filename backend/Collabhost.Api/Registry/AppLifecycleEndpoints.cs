using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
internal static class AppLifecycleEndpoints
{
    internal static async Task<IResult> StartAppAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProbeService probeService,
        RuntimeConfigFileWriter runtimeConfigFileWriter,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var hasProcess = typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = typeStore.HasBinding(app.AppTypeSlug, "routing");

        // Routing-only apps (e.g. static sites): enable route instead of starting a process
        if (!hasProcess && hasRouting)
        {
            // Render runtime-config-file BEFORE enabling the route (Card #336).
            // Ordering matters: if we enabled the route first, Caddy could serve a
            // stale on-disk value for an arbitrary window before the writer landed
            // the new file -- exactly the cutover bug (#236) #336 set out to fix.
            // Writer no-ops when the resolved Values is empty (preserves any
            // operator-maintained file on disk -- CLAUDE.md Rule 3).
            try
            {
                await runtimeConfigFileWriter.RenderAsync(app, ct);
            }
            catch (RuntimeConfigFileWriteException exception)
            {
                return TypedResults.Problem(exception.Message, statusCode: 409);
            }

            proxy.EnableRoute(app.Slug);
            proxy.RequestSync();

            // Clear the persisted operator-stop flag. Mirrors the process-bearing
            // Start path's write-through; routing-only apps need this so the
            // restored route state survives Collabhost restart. Card #350.
            await store.SetStoppedByOperatorAsync(app.Id, app.Slug, false, ct);

            await probeService.RunProbesAsync(app.Id, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppStarted,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var status = ProcessState.Running;
            var actions = AppEndpoints.BuildActions(hasProcess, hasRouting, status);

            return TypedResults.Ok
            (
                new AppActionResult(app.Id.ToString(), status.ToApiString(), actions)
            );
        }

        try
        {
            var managed = await supervisor.StartAppAsync(app.Id, ct);

            await probeService.RunProbesAsync(app.Id, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppStarted,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var actions = AppEndpoints.BuildActions(hasProcess, hasRouting, managed.State);

            return TypedResults.Ok
            (
                new AppActionResult(app.Id.ToString(), managed.State.ToApiString(), actions)
            );
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: 409);
        }
    }

    internal static async Task<IResult> StopAppAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var hasProcess = typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = typeStore.HasBinding(app.AppTypeSlug, "routing");

        // Routing-only apps (e.g. static sites): disable route instead of stopping a process
        if (!hasProcess && hasRouting)
        {
            proxy.DisableRoute(app.Slug);
            proxy.RequestSync();

            // Persist the operator-stop intent. Mirrors the process-bearing Stop
            // path's write-through (via ProcessSupervisor.StopAppAsync); routing-
            // only apps need this so the disabled route state survives Collabhost
            // restart. ProxyManager.HydrateRouteStatesFromPersistenceAsync reads
            // this column on boot. Card #350.
            await store.SetStoppedByOperatorAsync(app.Id, app.Slug, true, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppStopped,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var status = ProcessState.Stopped;
            var actions = AppEndpoints.BuildActions(hasProcess, hasRouting, status);

            return TypedResults.Ok
            (
                new AppActionResult(app.Id.ToString(), status.ToApiString(), actions)
            );
        }

        try
        {
            var managed = await supervisor.StopAppAsync(app.Id, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppStopped,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var actions = AppEndpoints.BuildActions(hasProcess, hasRouting, managed.State);

            return TypedResults.Ok
            (
                new AppActionResult(app.Id.ToString(), managed.State.ToApiString(), actions)
            );
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: 409);
        }
    }

    // Restart and kill are migrated to the operation spine (code-structure-conventions §8): the
    // endpoint is a thin adapter -- inject the concrete operation directly (no dispatcher), adapt
    // the route slug into the command, call it, and map OperationResult<AppActionOutcome> back to
    // exactly the AppActionResult / Problem the handler returned before. Start and stop still hold
    // their full bodies above; they migrate in PR 3 (dual-branch lifecycle).
    internal static async Task<IResult> RestartAppAsync
    (
        string slug,
        RestartAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new RestartAppCommand(slug), ct);

        return result.ToHttpResult();
    }

    internal static async Task<IResult> KillAppAsync
    (
        string slug,
        KillAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new KillAppCommand(slug), ct);

        return result.ToHttpResult();
    }
}

// File-scoped mapping from the surface-agnostic operation outcome back to the REST result shape
// (§7: the surface holds only its file-scoped mapping, never the contract types). This is the
// REST half of the outcome-mapping template PRs 3-7 copy. K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the success arm is
// gated on IsSuccess FIRST -- FailureKind is only read on the failure path. The three failure
// kinds map to the exact statuses the pre-migration handlers returned: NotFound -> 404 (empty
// body, as TypedResults.NotFound() did), Validation -> 400, Conflict -> 409 (the supervisor's
// InvalidOperationException, formerly the catch block, now hoisted to the Operation<,> base).
file static class AppLifecycleResultMapping
{
    public static IResult ToHttpResult(this OperationResult<AppActionOutcome> result)
    {
        if (result.IsSuccess)
        {
            var outcome = result.Value!;
            var actions = AppEndpoints.BuildActions(outcome.HasProcess, outcome.HasRouting, outcome.State);

            return TypedResults.Ok
            (
                new AppActionResult(outcome.Id.ToString(), outcome.State.ToApiString(), actions)
            );
        }

        return result.FailureKind switch
        {
            OperationFailureKind.NotFound => TypedResults.NotFound(),
            OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
            _ => TypedResults.Problem(result.Error, statusCode: 409),
        };
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
