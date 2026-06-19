using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.HealthChecks;
using Collabhost.Api.Operations;
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
        group.MapPost("/{slug}/start", AppLifecycleEndpoints.StartAppAsync);
        group.MapPost("/{slug}/stop", AppLifecycleEndpoints.StopAppAsync);
        group.MapPost("/{slug}/restart", AppLifecycleEndpoints.RestartAppAsync);
        group.MapPost("/{slug}/kill", AppLifecycleEndpoints.KillAppAsync);
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

    // DELETE /api/v1/apps/{slug} migrated to the operation spine (code-structure-conventions §8):
    // the stop-then-delete sequence -- stop-if-running (10s timeout + force-kill fallback),
    // routing-only route disable, delete, supervisor cleanup, probe-cache invalidation, app.deleted
    // event -- all moved into DeleteAppOperation. This endpoint is a thin adapter: adapt the route
    // slug into the command, call the injected operation directly (no dispatcher), and map
    // OperationResult<DeleteAppOutcome> back to exactly the empty 404 / 204 No Content the
    // pre-migration handler returned.
    private static async Task<IResult> DeleteAppAsync
    (
        string slug,
        DeleteAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new DeleteAppCommand(slug), ct);

        return result.ToHttpResult();
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

    internal static AppActions BuildActions(bool hasProcess, bool hasRouting, ProcessState status) =>
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

// File-scoped mapping from the surface-agnostic delete outcome back to the REST result shape (§7:
// the surface holds only its file-scoped mapping). K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the success arm is
// gated on IsSuccess FIRST -- FailureKind is only read on the failure path. The pre-migration
// handler returned an empty 204 No Content on success and an empty 404 on a missing slug; the
// operation only ever returns Success or NotFound, so those are the two arms that fire. Validation
// and Conflict are mapped defensively (the operation never produces them on the delete path) so a
// future operation change surfaces honestly rather than silently collapsing to 404.
file static class DeleteAppResultMapping
{
    // K-1: IsSuccess gates FIRST -- FailureKind (defaults to ordinal-0 NotFound) is read only in the
    // ternary's else branch, never on success. The single-statement success arm (NoContent) collapses
    // to a ternary per IDE0046 (the ReloadProxy precedent), unlike the lifecycle mapper whose
    // multi-statement success arm stays an if-block.
    public static IResult ToHttpResult(this OperationResult<DeleteAppOutcome> result) =>
        result.IsSuccess
            ? TypedResults.NoContent()
            : result.FailureKind switch
            {
                OperationFailureKind.NotFound => TypedResults.NotFound(),
                OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
                _ => TypedResults.Problem(result.Error, statusCode: 409),
            };
}
