using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
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

    internal static async Task<IResult> RestartAppAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
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

        try
        {
            var managed = await supervisor.RestartAppAsync(app.Id, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppRestarted,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var hasProcess = typeStore.HasBinding(app.AppTypeSlug, "process");
            var hasRouting = typeStore.HasBinding(app.AppTypeSlug, "routing");
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

    internal static async Task<IResult> KillAppAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
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

        try
        {
            await supervisor.KillAppAsync(app.Id, ct);

            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppKilled,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );

            var process = supervisor.GetProcess(app.Id);
            var state = process?.State ?? ProcessState.Stopped;

            var hasProcess = typeStore.HasBinding(app.AppTypeSlug, "process");
            var hasRouting = typeStore.HasBinding(app.AppTypeSlug, "routing");
            var actions = AppEndpoints.BuildActions(hasProcess, hasRouting, state);

            return TypedResults.Ok
            (
                new AppActionResult(app.Id.ToString(), state.ToApiString(), actions)
            );
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: 409);
        }
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
