using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.HealthChecks;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Shared;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Resources;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
public static class AppEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/apps").WithTags("Apps");

        group.MapGet("/", ListAppsAsync);
        group.MapPost("/", AppRegistrationEndpoints.CreateAppAsync);
        group.MapGet("/{slug}", GetAppDetailAsync);
        group.MapDelete("/{slug}", DeleteAppAsync);
        group.MapGet("/{slug}/settings", AppSettingsEndpoints.GetAppSettingsAsync);
        group.MapPut("/{slug}/settings", AppSettingsEndpoints.SaveAppSettingsAsync);
        group.MapPost("/{slug}/start", StartAppAsync);
        group.MapPost("/{slug}/stop", StopAppAsync);
        group.MapPost("/{slug}/restart", RestartAppAsync);
        group.MapPost("/{slug}/kill", KillAppAsync);
        group.MapGet("/{slug}/logs", GetAppLogsAsync);
        group.MapPost("/{slug}/runtime-config-file/import", ImportRuntimeConfigFileAsync);
    }

    private static async Task<IResult> ListAppsAsync
    (
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProxySettings proxySettings,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        var items = new List<AppListItem>();

        foreach (var app in apps)
        {
            var process = supervisor.GetProcess(app.Id);
            var bindings = typeStore.GetBindings(app.AppTypeSlug);
            var overrides = await store.GetOverridesAsync(app.Id, ct);

            var hasProcess = bindings?.ContainsKey("process") ?? false;
            var hasRouting = bindings?.ContainsKey("routing") ?? false;

            RoutingConfiguration? routingConfiguration = null;

            if (hasRouting && bindings is not null && bindings.TryGetValue("routing", out var routingBindingJson))
            {
                var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                    ? routingOverride.ConfigurationJson
                    : null;

                routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
                (
                    routingBindingJson, overrideJson
                );
            }

            var domain = routingConfiguration is not null
                ? CapabilityResolver.ResolveDomain(routingConfiguration.DomainPattern, app.Slug, proxySettings.BaseDomain)
                : null;

            var routeEnabled = routingConfiguration is not null && proxy.IsRouteEnabled(app.Slug);

            var status = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);

            var appTypeDefinition = typeStore.GetBySlug(app.AppTypeSlug);

            items.Add
            (
                new AppListItem
                (
                    app.Id.ToString(),
                    app.Slug,
                    app.DisplayName,
                    new AppTypeRef
                    (
                        appTypeDefinition?.Slug ?? app.AppTypeSlug,
                        appTypeDefinition?.DisplayName ?? app.AppTypeSlug
                    ),
                    status.ToApiString(),
                    domain,
                    routeEnabled,
                    process?.Port,
                    process?.UptimeSeconds,
                    new AppListActions
                    (
                        CanStart(hasProcess, hasRouting, status),
                        CanStop(hasProcess, hasRouting, status)
                    )
                )
            );
        }

        return TypedResults.Ok(items);
    }

    private static async Task<IResult> GetAppDetailAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProxySettings proxySettings,
        ProbeService probeService,
        IProcessResourceCache resourceCache,
        IHealthCheckExecutor healthCheckExecutor,
        AppDataPathResolver dataPathResolver,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var process = supervisor.GetProcess(app.Id);
        var bindings = typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await store.GetOverridesAsync(app.Id, ct);

        var hasProcess = bindings?.ContainsKey("process") ?? false;
        var hasRouting = bindings?.ContainsKey("routing") ?? false;
        var hasExternalTarget = bindings?.ContainsKey("external-target") ?? false;

        // Routing
        RoutingConfiguration? routingConfiguration = null;

        if (hasRouting && bindings is not null && bindings.TryGetValue("routing", out var routingBindingJson))
        {
            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBindingJson, overrideJson
            );
        }

        var domain = routingConfiguration is not null
            ? CapabilityResolver.ResolveDomain(routingConfiguration.DomainPattern, app.Slug, proxySettings.BaseDomain)
            : null;

        var routeEnabled = routingConfiguration is not null && proxy.IsRouteEnabled(app.Slug);

        var status = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);

        // Restart policy + auto-start
        string? restartPolicyValue = null;

        if (bindings is not null && bindings.TryGetValue("restart", out var restartBindingJson))
        {
            var overrideJson = overrides.TryGetValue("restart", out var restartOverride)
                ? restartOverride.ConfigurationJson
                : null;

            var restartConfiguration = CapabilityResolver.Resolve<RestartConfiguration>
            (
                restartBindingJson, overrideJson
            );

            restartPolicyValue = restartConfiguration.Policy.ToString();
            restartPolicyValue = char.ToLowerInvariant(restartPolicyValue[0])
                + restartPolicyValue[1..];
        }

        bool? autoStartValue = null;

        if (bindings is not null && bindings.TryGetValue("auto-start", out var autoStartBindingJson))
        {
            var overrideJson = overrides.TryGetValue("auto-start", out var autoStartOverride)
                ? autoStartOverride.ConfigurationJson
                : null;

            var autoStartConfiguration = CapabilityResolver.Resolve<AutoStartConfiguration>
            (
                autoStartBindingJson, overrideJson
            );

            autoStartValue = autoStartConfiguration.Enabled;
        }

        // Probes -- cached probe results plus lifecycle state (Card #337).
        var probeResult = probeService.GetCachedProbes(app.Id, app.AppTypeSlug);
        var probesStatus = probeResult.Status.ToApiString();
        var probes = probeResult.Entries;

        // Route info
        AppRoute? route = null;

        if (routingConfiguration is not null && domain is not null)
        {
            string target;

            if (routingConfiguration.ServeMode == ServeMode.ReverseProxy)
            {
                if (hasExternalTarget)
                {
                    // External-target apps (Card #348): the upstream is operator-
                    // declared, surface the resolved host:port so the App Detail
                    // page tells the operator where the proxy actually dials --
                    // not a misleading "localhost:..." that does not exist.
                    var externalTarget = bindings is not null
                        && bindings.TryGetValue("external-target", out var externalTargetBinding)
                            ? CapabilityResolver.Resolve<ExternalTargetConfiguration>
                            (
                                externalTargetBinding,
                                overrides.TryGetValue("external-target", out var externalTargetOverride)
                                    ? externalTargetOverride.ConfigurationJson
                                    : null
                            )
                            : null;

                    target = externalTarget is not null
                        && !string.IsNullOrWhiteSpace(externalTarget.Host)
                        && externalTarget.Port > 0
                            ? string.Format
                            (
                                CultureInfo.InvariantCulture,
                                "{0}://{1}:{2}",
                                externalTarget.Scheme,
                                externalTarget.Host,
                                externalTarget.Port
                            )
                            : "not-configured";
                }
                else
                {
                    target = process?.Port is not null
                        ? $"localhost:{process.Port.Value.ToString(CultureInfo.InvariantCulture)}"
                        : "not-running";
                }
            }
            else
            {
                target = routingConfiguration.ServeMode == ServeMode.FileServer
                    ? "file-server"
                    : "not-running";
            }

            // TLS posture is derived from the proxy's configured listen surface (Card
            // #263 item 1.3). Today this evaluates to true on every supported install
            // (default ListenAddress = ":80,:443"); deriving makes the response honest
            // if an operator ever pins ListenAddress to plain :80.
            var tls = ProxyConfigurationBuilder.HasTlsListener(proxySettings.ListenAddress);

            route = new AppRoute(domain, target, tls);
        }

        var actions = BuildActions(hasProcess, hasRouting, status);

        var appTypeDefinition = typeStore.GetBySlug(app.AppTypeSlug);

        // Health status -- pulled from the executor's cache. Null when the
        // capability is not bound for this app type, when there is no live
        // probe target, or when no probe has run yet. The frontend renders
        // null as "--".
        //
        // Two live-probe shapes today (Card #348):
        //   (a) Supervised-process app, process running -- the existing shape.
        //   (b) External-route app, route enabled -- the executor's TickAsync
        //       gates on IsRouteEnabled, so a disabled external-route's
        //       cache entry is cleared. We still gate at the surface so a
        //       race (probe wrote, then operator disabled) doesn't show a
        //       stale value.
        string? healthStatus = null;

        var liveProbeTarget =
            (hasProcess && process is not null && process.IsRunning)
            || (hasExternalTarget && hasRouting && routeEnabled);

        if (liveProbeTarget)
        {
            var healthResult = healthCheckExecutor.GetLatest(app.Id);

            if (healthResult is not null)
            {
                healthStatus = healthResult.Status.ToApiString();
            }
        }

        // Resources -- pulled from the resource sampler's cache. Null when the
        // process is not running or no snapshot has been taken yet (sampler runs on
        // a 5-second cadence). Note that CpuPercent is null on the very first sample
        // for a PID; MemoryMb and HandleCount are populated on the first sample.
        AppResources? resources = null;

        if (process is not null && process.IsRunning)
        {
            var snapshot = resourceCache.GetLatest(app.Id);

            if (snapshot is not null)
            {
                resources = new AppResources
                (
                    snapshot.CpuPercent,
                    snapshot.MemoryMb,
                    snapshot.HandleCount
                );
            }
        }

        var tabs = ResolveTabs(app.AppTypeSlug);

        var detail = new AppDetail
        (
            app.Id.ToString(),
            app.Slug,
            app.DisplayName,
            new AppTypeDetailRef
            (
                appTypeDefinition?.Slug ?? app.AppTypeSlug,
                appTypeDefinition?.DisplayName ?? app.AppTypeSlug
            ),
            app.RegisteredAt.ToString("o", CultureInfo.InvariantCulture),
            status.ToApiString(),
            process?.Pid,
            process?.Port,
            process?.UptimeSeconds,
            process?.RestartCount ?? 0,
            restartPolicyValue,
            autoStartValue,
            domain,
            routeEnabled,
            healthStatus,
            probesStatus,
            probes,
            resources,
            route,
            actions,
            dataPathResolver.ResolveFor(app.Slug),
            tabs
        );

        return TypedResults.Ok(detail);
    }

    // Backend-authoritative App Detail tabs (Card #348, D5). Centralized so
    // every future AppType only updates this one switch; the FE renders what
    // the backend says without re-deriving from appType.slug or actions. The
    // switch matches every built-in slug today; unknown slugs default to the
    // process-app shape rather than rendering an empty tab strip on an
    // unrecognized type (preserves existing FE behavior pre-#348).
    internal static IReadOnlyList<string> ResolveTabs(string appTypeSlug) =>
        appTypeSlug switch
        {
            "dotnet-app" or "nodejs-app" or "executable" => ["logs", "technology"],
            "static-site" => ["logs", "technology"],
            "system-service" => ["logs"],
            "internal-service" => ["logs"],
            "external-route" => ["health", "route"],
            _ => ["logs", "technology"]
        };

    private static async Task<IResult> StartAppAsync
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
            var actions = BuildActions(hasProcess, hasRouting, status);

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

            var actions = BuildActions(hasProcess, hasRouting, managed.State);

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

    private static async Task<IResult> StopAppAsync
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
            var actions = BuildActions(hasProcess, hasRouting, status);

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

            var actions = BuildActions(hasProcess, hasRouting, managed.State);

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

    private static async Task<IResult> RestartAppAsync
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
            var actions = BuildActions(hasProcess, hasRouting, managed.State);

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

    private static async Task<IResult> KillAppAsync
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
            var actions = BuildActions(hasProcess, hasRouting, state);

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

    // POST /api/v1/apps/{slug}/runtime-config-file/import (Card #336).
    //
    // Reads the existing config file on disk (typically <artifactDir>/config.json)
    // and returns its flat string->string top-level entries plus a list of any
    // skipped non-flat entries (nested objects, arrays, nulls, non-string
    // primitives). The response is a preview only -- it does NOT persist the
    // values; the operator reviews and saves them via the normal settings-save
    // flow. Bill ruling S55 #6: flat-JSON only, warn about skipped entries.
    private static async Task<IResult> ImportRuntimeConfigFileAsync
    (
        string slug,
        AppStore store,
        RuntimeConfigFileWriter runtimeConfigFileWriter,
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
            var result = await runtimeConfigFileWriter.ImportFromDiskAsync(app, ct);

            return TypedResults.Ok
            (
                new RuntimeConfigFileImportResponse
                (
                    result.Imported,
                    result.Skipped,
                    result.SourcePath
                )
            );
        }
        catch (RuntimeConfigFileWriteException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: 400);
        }
    }

    private static async Task<IResult> GetAppLogsAsync
    (
        string slug,
        AppStore store,
        ProcessSupervisor supervisor,
        int? lines,
        string? stream,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var buffer = supervisor.GetOrCreateLogBuffer(app.Id);
        var lineCount = lines ?? 200;
        var allEntries = buffer.GetLastWithIds(lineCount);

        LogStream? filterStream = stream?.ToLowerInvariant() switch
        {
            "stdout" => LogStream.StdOut,
            "stderr" => LogStream.StdErr,
            _ => null
        };

        var filtered = filterStream.HasValue
            ? allEntries.Where(e => e.Item.Stream == filterStream.Value)
            : allEntries;

        var entries = filtered
                .Select
                (
                    e => new LogEntryResponse
                    (
                        e.Id,
                        e.Item.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                        e.Item.Stream == LogStream.StdOut ? "stdout" : "stderr",
                        e.Item.Content,
                        e.Item.Level
                    )
                )
                    .ToList();

        return TypedResults.Ok(new LogsResponse(entries, buffer.Count));
    }

    private static async Task<IResult> DeleteAppAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProbeService probeService,
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

        // Capture before delete -- app won't exist after store.DeleteAppAsync
        var appId = app.Id.ToString();
        var appSlug = app.Slug;
        var appDisplayName = app.DisplayName;
        var appTypeSlug = app.AppTypeSlug;

        // Stop if running (10s timeout, force-kill fallback)
        var process = supervisor.GetProcess(app.Id);

        if (process is not null && process.IsRunning)
        {
            try
            {
                using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                await supervisor.StopAppAsync(app.Id, timeoutCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout expired -- force kill
                try
                {
                    await supervisor.KillAppAsync(app.Id, CancellationToken.None);
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

        // Routing-only apps (static-site, external-route): explicitly disable
        // the route before the row is deleted. Without this, the route survives
        // in Caddy until the next SyncRoutesAsync pass re-derives the live set
        // from AppStore.ListAsync -- a small window where Caddy still routes to
        // an app that no longer exists. Card #348 fix-along; in-scope per
        // CLAUDE.md Rule 9 marginal-cost test (the PR already touches the
        // proxy surface, the delete site is the natural symmetric closure).
        var hasProcess = typeStore.HasBinding(appTypeSlug, "process");
        var hasRouting = typeStore.HasBinding(appTypeSlug, "routing");

        if (!hasProcess && hasRouting)
        {
            proxy.DisableRoute(appSlug);
            proxy.RequestSync();
        }

        await store.DeleteAppAsync(app.Id, ct);

        supervisor.CleanupDeletedApp(app.Id, appSlug);

        // Early hygiene on delete -- release this app's cached probe entry
        // immediately rather than letting it squat in the cache until the next
        // periodic prune tick. The cache is keyed by ULID, so a recreated app
        // with the same slug always gets a fresh entry regardless. Card #337
        // fix-along.
        probeService.InvalidateProbeCache(app.Id);

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.AppDeleted,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = appId,
                AppSlug = appSlug,
                MetadataJson = JsonSerializer.Serialize(new { displayName = appDisplayName })
            },
            ct
        );

        return TypedResults.NoContent();
    }

    internal static ProcessState ResolveStatus
    (
        bool hasProcess,
        ManagedProcess? process,
        bool hasRouting,
        bool routeEnabled
    ) =>
        hasProcess
            ? process?.State ?? ProcessState.Stopped
            : hasRouting && routeEnabled
                ? ProcessState.Running
                : ProcessState.Stopped;

    private static bool CanStart(bool hasProcess, bool hasRouting, ProcessState status) =>
        (hasProcess || hasRouting) && status is ProcessState.Stopped or ProcessState.Crashed or ProcessState.Fatal;

    private static bool CanStop(bool hasProcess, bool hasRouting, ProcessState status) =>
        (hasProcess || hasRouting) && status == ProcessState.Running;

    private static AppActions BuildActions(bool hasProcess, bool hasRouting, ProcessState status) =>
        new
        (
            CanStart(hasProcess, hasRouting, status),
            CanStop(hasProcess, hasRouting, status),
            hasProcess && status == ProcessState.Running,
            hasProcess && status is ProcessState.Running or ProcessState.Starting or ProcessState.Restarting
        );
}
#pragma warning restore MA0011
#pragma warning restore MA0076
