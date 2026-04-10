using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
public static class AppEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/apps").WithTags("Apps");

        group.MapGet("/", ListAppsAsync);
        group.MapPost("/", CreateAppAsync);
        group.MapGet("/{slug}", GetAppDetailAsync);
        group.MapDelete("/{slug}", DeleteAppAsync);
        group.MapGet("/{slug}/settings", GetAppSettingsAsync);
        group.MapPut("/{slug}/settings", SaveAppSettingsAsync);
        group.MapPost("/{slug}/start", StartAppAsync);
        group.MapPost("/{slug}/stop", StopAppAsync);
        group.MapPost("/{slug}/restart", RestartAppAsync);
        group.MapPost("/{slug}/kill", KillAppAsync);
        group.MapGet("/{slug}/logs", GetAppLogsAsync);
    }

    private static async Task<IResult> ListAppsAsync
    (
        AppStore store,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        var items = new List<AppListItem>();

        foreach (var app in apps)
        {
            var process = supervisor.GetProcess(app.Id);
            var bindings = await store.GetBindingsAsync(app.AppTypeId, ct);
            var overrides = await store.GetOverridesAsync(app.Id, ct);

            var hasProcess = bindings.Any
            (
                b => string.Equals(b.CapabilitySlug, "process", StringComparison.Ordinal)
            );

            var hasRouting = bindings.Any
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            var routingBinding = bindings.SingleOrDefault
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            RoutingConfiguration? routingConfiguration = null;

            if (routingBinding is not null)
            {
                var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                    ? routingOverride.ConfigurationJson
                    : null;

                routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
                (
                    routingBinding.DefaultConfigurationJson, overrideJson
                );
            }

            var domain = routingConfiguration?.DomainPattern
                .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

            var routeEnabled = routingConfiguration is not null && proxy.IsRouteEnabled(app.Slug);

            var status = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);

            items.Add
            (
                new AppListItem
                (
                    app.Id.ToString(),
                    app.Slug,
                    app.DisplayName,
                    new AppTypeRef(app.AppType.Slug, app.AppType.DisplayName),
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
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProbeService probeService,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var process = supervisor.GetProcess(app.Id);
        var bindings = await store.GetBindingsAsync(app.AppTypeId, ct);
        var overrides = await store.GetOverridesAsync(app.Id, ct);

        var hasProcess = bindings.Any
        (
            b => string.Equals(b.CapabilitySlug, "process", StringComparison.Ordinal)
        );

        var hasRouting = bindings.Any
        (
            b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
        );

        // Routing
        var routingBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
        );

        RoutingConfiguration? routingConfiguration = null;

        if (routingBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBinding.DefaultConfigurationJson, overrideJson
            );
        }

        var domain = routingConfiguration?.DomainPattern
            .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

        var routeEnabled = routingConfiguration is not null && proxy.IsRouteEnabled(app.Slug);

        var status = ResolveStatus(hasProcess, process, hasRouting, routeEnabled);

        // Restart policy + auto-start
        string? restartPolicyValue = null;

        var restartBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "restart", StringComparison.Ordinal)
        );

        if (restartBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("restart", out var restartOverride)
                ? restartOverride.ConfigurationJson
                : null;

            var restartConfiguration = CapabilityResolver.Resolve<RestartConfiguration>
            (
                restartBinding.DefaultConfigurationJson, overrideJson
            );

            restartPolicyValue = restartConfiguration.Policy.ToString();
            restartPolicyValue = char.ToLowerInvariant(restartPolicyValue[0])
                + restartPolicyValue[1..];
        }

        bool? autoStartValue = null;

        var autoStartBinding = bindings.SingleOrDefault
        (
            b => string.Equals(b.CapabilitySlug, "auto-start", StringComparison.Ordinal)
        );

        if (autoStartBinding is not null)
        {
            var overrideJson = overrides.TryGetValue("auto-start", out var autoStartOverride)
                ? autoStartOverride.ConfigurationJson
                : null;

            var autoStartConfiguration = CapabilityResolver.Resolve<AutoStartConfiguration>
            (
                autoStartBinding.DefaultConfigurationJson, overrideJson
            );

            autoStartValue = autoStartConfiguration.Enabled;
        }

        // Probes -- cached probe results from in-memory cache
        var probes = probeService.GetCachedProbes(app.Id);

        // Route info
        AppRoute? route = null;

        if (routingConfiguration is not null && domain is not null)
        {
            var target = routingConfiguration.ServeMode == ServeMode.ReverseProxy && process?.Port is not null
                ? $"localhost:{process.Port.Value.ToString(CultureInfo.InvariantCulture)}"
                : routingConfiguration.ServeMode == ServeMode.FileServer
                    ? "file-server"
                    : "not-running";

            route = new AppRoute(domain, target, true);
        }

        var actions = BuildActions(hasProcess, hasRouting, status);

        var detail = new AppDetail
        (
            app.Id.ToString(),
            app.Slug,
            app.DisplayName,
            new AppTypeDetailRef
            (
                app.AppType.Id.ToString(),
                app.AppType.Slug,
                app.AppType.DisplayName
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
            null,
            probes,
            null,
            route,
            actions
        );

        return TypedResults.Ok(detail);
    }

    private static async Task<IResult> GetAppSettingsAsync
    (
        string slug,
        AppStore store,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var bindings = await store.GetBindingsAsync(app.AppTypeId, ct);
        var overrides = await store.GetOverridesAsync(app.Id, ct);

        var sections = BuildSettingsSections(app, bindings, overrides);

        var settings = new AppSettings
        (
            app.Id.ToString(),
            app.Slug,
            app.DisplayName,
            app.AppType.DisplayName,
            app.RegisteredAt.ToString("o", CultureInfo.InvariantCulture),
            sections
        );

        return TypedResults.Ok(settings);
    }

    private static async Task<IResult> SaveAppSettingsAsync
    (
        string slug,
        UpdateSettingsRequest request,
        AppStore store,
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

        var bindings = await store.GetBindingsAsync(app.AppTypeId, ct);
        var overrides = await store.GetOverridesAsync(app.Id, ct);

        // Handle identity section changes
        if (request.Changes.TryGetValue("identity", out var identityChanges))
        {
            if (identityChanges.TryGetValue("displayName", out var displayNameElement))
            {
                var newDisplayName = displayNameElement.GetString();

                if (!string.IsNullOrWhiteSpace(newDisplayName))
                {
                    app.DisplayName = newDisplayName;
                    app.ModifiedAt = DateTime.UtcNow;

                    await store.UpdateAppAsync(app, ct);
                }
            }
        }

        // Handle capability section changes
        foreach (var (sectionKey, sectionChanges) in request.Changes)
        {
            if (string.Equals(sectionKey, "identity", StringComparison.Ordinal))
            {
                continue;
            }

            var binding = bindings.SingleOrDefault
            (
                b => string.Equals(b.CapabilitySlug, sectionKey, StringComparison.Ordinal)
            );

            if (binding is null)
            {
                continue;
            }

            // Validate edits against schema
            var proposedOverrides = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                proposedOverrides[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            var validationErrors = CapabilityResolver.ValidateEdits
            (
                sectionKey, proposedOverrides, isNewApp: false
            );

            if (validationErrors.Count > 0)
            {
                return TypedResults.Problem
                (
                    string.Join("; ", validationErrors),
                    statusCode: 400
                );
            }

            // Merge with existing override or create new one
            var existingOverrideJson = overrides.TryGetValue(sectionKey, out var existing)
                ? existing.ConfigurationJson
                : null;

            var effectiveOverride = existingOverrideJson is not null
                ? JsonNode.Parse(existingOverrideJson)?.AsObject() ?? []
                : (JsonObject)[];

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                effectiveOverride[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            await store.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                effectiveOverride.ToJsonString(_jsonOptions),
                ct
            );
        }

        // Refresh and return full settings
        store.Invalidate(slug);
        store.InvalidateOverrides(app.Id);

        // Re-probe when artifact config changes (location or project root)
        if (request.Changes.ContainsKey("artifact"))
        {
            probeService.InvalidateProbeCache(app.Id);

            await probeService.RunProbesAsync(app.Id, ct);
        }

        var changedCapabilities = request.Changes.Keys
            .Where(k => !string.Equals(k, "identity", StringComparison.Ordinal))
                .ToList();

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.AppSettingsUpdated,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = app.Id.ToString(),
                AppSlug = app.Slug,
                MetadataJson = JsonSerializer.Serialize(new { changedCapabilities })
            },
            ct
        );

        var freshApp = await store.GetBySlugAsync(slug, ct);
        var freshBindings = await store.GetBindingsAsync(app.AppTypeId, ct);
        var freshOverrides = await store.GetOverridesAsync(app.Id, ct);

        var sections = BuildSettingsSections(freshApp!, freshBindings, freshOverrides);

        var settings = new AppSettings
        (
            freshApp!.Id.ToString(),
            freshApp.Slug,
            freshApp.DisplayName,
            freshApp.AppType.DisplayName,
            freshApp.RegisteredAt.ToString("o", CultureInfo.InvariantCulture),
            sections
        );

        return TypedResults.Ok(settings);
    }

    private static async Task<IResult> StartAppAsync
    (
        string slug,
        AppStore store,
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

        var hasProcess = await store.HasBindingAsync(app.AppTypeId, "process", ct);
        var hasRouting = await store.HasBindingAsync(app.AppTypeId, "routing", ct);

        // Routing-only apps (e.g. static sites): enable route instead of starting a process
        if (!hasProcess && hasRouting)
        {
            proxy.EnableRoute(app.Slug);
            proxy.RequestSync();

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

        var hasProcess = await store.HasBindingAsync(app.AppTypeId, "process", ct);
        var hasRouting = await store.HasBindingAsync(app.AppTypeId, "routing", ct);

        // Routing-only apps (e.g. static sites): disable route instead of stopping a process
        if (!hasProcess && hasRouting)
        {
            proxy.DisableRoute(app.Slug);
            proxy.RequestSync();

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

            var hasProcess = await store.HasBindingAsync(app.AppTypeId, "process", ct);
            var hasRouting = await store.HasBindingAsync(app.AppTypeId, "routing", ct);
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

            var hasProcess = await store.HasBindingAsync(app.AppTypeId, "process", ct);
            var hasRouting = await store.HasBindingAsync(app.AppTypeId, "routing", ct);
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

    private static async Task<IResult> CreateAppAsync
    (
        CreateAppRequest request,
        AppStore store,
        ProxyManager proxy,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        var (isValid, error) = Slug.Validate(request.Name);

        if (!isValid)
        {
            return TypedResults.Problem(error, statusCode: 400);
        }

        var exists = await store.ExistsBySlugAsync(request.Name, ct);

        if (exists)
        {
            return TypedResults.Problem
            (
                $"An app with slug '{request.Name}' already exists.",
                statusCode: 409
            );
        }

        if (!Ulid.TryParse(request.AppTypeId, out var appTypeId))
        {
            return TypedResults.Problem("Invalid app type ID.", statusCode: 400);
        }

        var appType = await store.GetAppTypeByIdAsync(appTypeId, ct);

        if (appType is null)
        {
            return TypedResults.Problem("App type not found.", statusCode: 404);
        }

        // Validate all settings BEFORE creating the app to ensure registration is transactional
        var validatedOverrides = new List<(string SectionKey, JsonObject Overrides)>();

        if (request.Values is not null)
        {
            // Collect process overrides from both the "process" section and the "discovery" virtual section
            JsonObject? processOverrides = null;

            foreach (var (sectionKey, sectionValues) in request.Values)
            {
                // The "discovery" section is a registration-only concept that maps to the "process" capability
                if (string.Equals(sectionKey, "discovery", StringComparison.Ordinal))
                {
                    processOverrides ??= [];

                    foreach (var (fieldKey, fieldValue) in sectionValues)
                    {
                        processOverrides[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
                    }

                    continue;
                }

                var overrideObject = new JsonObject();

                foreach (var (fieldKey, fieldValue) in sectionValues)
                {
                    overrideObject[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
                }

                var validationErrors = CapabilityResolver.ValidateEdits
                (
                    sectionKey, overrideObject, isNewApp: true
                );

                if (validationErrors.Count > 0)
                {
                    return TypedResults.Problem
                    (
                        string.Join("; ", validationErrors),
                        statusCode: 400
                    );
                }

                // If this is the process section, merge with any discovery overrides
                if (string.Equals(sectionKey, "process", StringComparison.Ordinal))
                {
                    processOverrides ??= [];

                    foreach (var property in overrideObject)
                    {
                        processOverrides[property.Key] = property.Value?.DeepClone();
                    }

                    continue;
                }

                validatedOverrides.Add((sectionKey, overrideObject));
            }

            // Validate merged process overrides
            if (processOverrides is not null)
            {
                var processErrors = CapabilityResolver.ValidateEdits
                (
                    "process", processOverrides, isNewApp: true
                );

                if (processErrors.Count > 0)
                {
                    return TypedResults.Problem
                    (
                        string.Join("; ", processErrors),
                        statusCode: 400
                    );
                }

                validatedOverrides.Add(("process", processOverrides));
            }
        }

        // All validation passed -- now create the app and persist overrides
        var app = new App
        {
            Slug = request.Name.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName,
            AppTypeId = appTypeId
        };

        await store.CreateAsync(app, ct);

        foreach (var (sectionKey, overrideObject) in validatedOverrides)
        {
            await store.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                overrideObject.ToJsonString(_jsonOptions),
                ct
            );
        }

        // Routing-only apps (e.g. static sites) start with their route disabled.
        // Process-based apps have routes tied to process state, so no explicit disable needed.
        var hasRouting = await store.HasBindingAsync(appTypeId, "routing", ct);
        var hasProcess = await store.HasBindingAsync(appTypeId, "process", ct);

        if (hasRouting && !hasProcess)
        {
            proxy.DisableRoute(app.Slug);
        }

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.AppCreated,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = app.Id.ToString(),
                AppSlug = app.Slug,
                MetadataJson = JsonSerializer.Serialize
                (
                    new { appTypeSlug = appType.Slug, displayName = app.DisplayName }
                )
            },
            ct
        );

        return TypedResults.Created
        (
            $"/api/v1/apps/{app.Slug}",
            new CreateAppResponse(app.Id.ToString())
        );
    }

    private static async Task<IResult> DeleteAppAsync
    (
        string slug,
        AppStore store,
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

        // Capture before delete -- app won't exist after store.DeleteAppAsync
        var appId = app.Id.ToString();
        var appSlug = app.Slug;
        var appDisplayName = app.DisplayName;

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

        await store.DeleteAppAsync(app.Id, ct);

        supervisor.CleanupDeletedApp(app.Id);

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

    private static ProcessState ResolveStatus
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
            hasProcess && status is ProcessState.Running or ProcessState.Starting or ProcessState.Restarting,
            false
        );

    private static List<SettingsSection> BuildSettingsSections
    (
        App app,
        IReadOnlyList<CapabilityBinding> bindings,
        IReadOnlyDictionary<string, CapabilityOverride> overrides
    )
    {
        var sections = new List<SettingsSection>
        {
            // Identity section (always first)
            new            (
                "identity",
                "Identity",
                [
                    new SettingsField
                    (
                        "name",
                        "Name (slug)",
                        "text",
                        app.Slug,
                        app.Slug,
                        new FieldEditableLocked("Set during registration")
                    ),
                    new SettingsField
                    (
                        "displayName",
                        "Display Name",
                        "text",
                        app.DisplayName,
                        app.DisplayName,
                        new FieldEditableAlways()
                    )
                ]
            )
        };

        // Capability sections
        foreach (var binding in bindings.OrderBy(b => b.CapabilitySlug.GetCapabilityOrder()))
        {
            var definition = CapabilityCatalog.Get(binding.CapabilitySlug);

            if (definition is null)
            {
                continue;
            }

            var overrideJson = overrides.TryGetValue(binding.CapabilitySlug, out var capabilityOverride)
                ? capabilityOverride.ConfigurationJson
                : null;

            var effectiveJson = CapabilityResolver.ResolveJson
            (
                binding.DefaultConfigurationJson, overrideJson
            );

            JsonObject? effectiveValues = null;
            JsonObject? defaultValues = null;

            try
            {
                effectiveValues = JsonNode.Parse(effectiveJson)?.AsObject();
                defaultValues = JsonNode.Parse(binding.DefaultConfigurationJson)?.AsObject();
            }
            catch (JsonException)
            {
                continue;
            }

            if (effectiveValues is null || defaultValues is null)
            {
                continue;
            }

            var fields = new List<SettingsField>();

            foreach (var fieldDescriptor in definition.Schema)
            {
                var value = effectiveValues.GetFieldValue(fieldDescriptor.Key);
                var defaultValue = defaultValues.GetFieldValue(fieldDescriptor.Key);

                fields.Add
                (
                    new SettingsField
                    (
                        fieldDescriptor.Key,
                        fieldDescriptor.Label,
                        fieldDescriptor.Type.ToString().ToLowerInvariant(),
                        value,
                        defaultValue,
                        fieldDescriptor.Editable,
                        fieldDescriptor.RequiresRestart,
                        fieldDescriptor.Options?
                            .Select(o => new FieldOption(o.Value.ToCamelCase(), o.Label))
                                .ToList(),
                        fieldDescriptor.HelpText,
                        fieldDescriptor.Unit
                    )
                );
            }

            sections.Add
            (
                new SettingsSection
                (
                    binding.CapabilitySlug,
                    definition.DisplayName,
                    fields
                )
            );
        }

        return sections;
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076

file static class AppEndpointExtensions
{
    extension(JsonObject obj)
    {
        public object? GetFieldValue(string key) =>
            !obj.TryGetPropertyValue(key, out var node) || node is null
                ? null
                : node.GetValueKind() switch
                {
                    JsonValueKind.String => node.GetValue<string>(),
                    JsonValueKind.Number => node.GetValue<double>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, string>>(node),
                    _ => null
                };
    }

    extension(string value)
    {
        public string ToCamelCase() =>
            string.IsNullOrEmpty(value)
                ? value
                : char.ToLowerInvariant(value[0]) + value[1..];

        public int GetCapabilityOrder() => value switch
        {
            "process" => 0,
            "port-injection" => 1,
            "routing" => 2,
            "health-check" => 3,
            "restart" => 4,
            "auto-start" => 5,
            "environment-defaults" => 6,
            "artifact" => 7,
            _ => 99
        };
    }
}
