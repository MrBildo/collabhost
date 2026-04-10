using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
[McpServerToolType]
public class LifecycleTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore,
    ILogger<LifecycleTools> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<LifecycleTools> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    [McpServerTool
    (
        Name = "start_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Starts a registered application. For process-based apps (dotnet-app, nodejs-app, executable), this spawns the process and allocates a port. For static sites, this enables the Caddy proxy route (no process is involved). The app must be in 'stopped' or 'crashed' status. Returns immediately after initiating the start. The process may take a few seconds to reach 'running' status. Use get_app to check the current status. Starting an already-running app is a safe no-op.")]
    public async Task<CallToolResult> StartAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug!, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug!, "routing");

        // Routing-only apps (e.g. static sites): enable route instead of starting a process
        if (!hasProcess && hasRouting)
        {
            _proxy.EnableRoute(app.Slug);
            _proxy.RequestSync();

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppStarted,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.started (slug={Slug})", app.Slug);
            }

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status = "running", appType = app.AppTypeSlug }
                )
            );
        }

        try
        {
            var managed = await _supervisor.StartAppAsync(app.Id, ct);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppStarted,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.started (slug={Slug})", app.Slug);
            }

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status = managed.State.ToApiString(), appType = app.AppTypeSlug }
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return McpResponseFormatter.InvalidParameters(ex.Message);
        }
    }

    [McpServerTool
    (
        Name = "stop_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Stops a running application. For process-based apps, this sends a graceful shutdown signal (CTRL+C / SIGTERM). For static sites, this disables the Caddy proxy route. Returns immediately after initiating the stop. Use get_app to check the current status. Stopping an already-stopped app is a safe no-op.")]
    public async Task<CallToolResult> StopAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug!, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug!, "routing");

        // Routing-only apps (e.g. static sites): disable route instead of stopping a process
        if (!hasProcess && hasRouting)
        {
            _proxy.DisableRoute(app.Slug);
            _proxy.RequestSync();

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppStopped,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.stopped (slug={Slug})", app.Slug);
            }

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status = "stopped", appType = app.AppTypeSlug }
                )
            );
        }

        try
        {
            var managed = await _supervisor.StopAppAsync(app.Id, ct);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppStopped,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.stopped (slug={Slug})", app.Slug);
            }

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status = managed.State.ToApiString(), appType = app.AppTypeSlug }
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return McpResponseFormatter.InvalidParameters(ex.Message);
        }
    }

    [McpServerTool
    (
        Name = "restart_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Restarts a running process-based application (stop then start). Only works for process-based apps, not static sites. The app should be in 'running' status. Use get_app to check status after calling.")]
    public async Task<CallToolResult> RestartAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug!, "process");

        if (!hasProcess)
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Cannot restart app '{slug}': only process-based apps support restart. '{slug}' is a {app.AppTypeSlug}."
            );
        }

        try
        {
            var managed = await _supervisor.RestartAppAsync(app.Id, ct);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppRestarted,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.restarted (slug={Slug})", app.Slug);
            }

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status = managed.State.ToApiString(), appType = app.AppTypeSlug }
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return McpResponseFormatter.InvalidParameters(ex.Message);
        }
    }

    [McpServerTool
    (
        Name = "kill_app",
        ReadOnly = false,
        Destructive = true,
        Idempotent = false,
        OpenWorld = false
    )]
    [Description("Force-kills a running process immediately without graceful shutdown. Use this only when stop_app has been tried and the process is unresponsive. Unsaved state in the process will be lost. Only works for process-based apps. IMPORTANT: Always try stop_app first. kill_app is a last resort.")]
    public async Task<CallToolResult> KillAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug!, "process");

        if (!hasProcess)
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Cannot kill app '{slug}': only process-based apps support kill. '{slug}' is a {app.AppTypeSlug}."
            );
        }

        try
        {
            await _supervisor.KillAppAsync(app.Id, ct);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppKilled,
                        ActorId = _currentUser.UserId.ToString(),
                        ActorName = _currentUser.User.Name,
                        AppId = app.Id.ToString(),
                        AppSlug = app.Slug,
                        MetadataJson = null
                    },
                    ct
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for app.killed (slug={Slug})", app.Slug);
            }

            var process = _supervisor.GetProcess(app.Id);
            var status = process?.State.ToApiString() ?? "stopped";

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = app.Slug, status, appType = app.AppTypeSlug }
                )
            );
        }
        catch (InvalidOperationException ex)
        {
            return McpResponseFormatter.InvalidParameters(ex.Message);
        }
    }

    [McpServerTool
    (
        Name = "get_logs",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Returns application log entries from the in-memory ring buffer. Logs include stdout and stderr output captured from the application's process. Returns entries in chronological order. Do not print the full log output in your response to the user -- summarize key findings instead. Use logs to diagnose why an application is not running or is behaving unexpectedly. The app must have been started at least once to have log entries.")]
    public async Task<CallToolResult> GetLogsAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        [Description("Maximum number of log entries to return (1-500). Defaults to 100.")] int? limit,
        [Description("Entries to skip from the start of the buffer for pagination. Defaults to 0.")] int? offset,
        CancellationToken ct
    )
    {
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var effectiveLimit = Math.Clamp(limit ?? 100, 1, 500);

        var buffer = _supervisor.GetOrCreateLogBuffer(app.Id);

        var allEntries = buffer.GetLastWithIds(buffer.Count);

        var effectiveOffset = offset ?? 0;

        var page = allEntries
            .Skip(effectiveOffset)
            .Take(effectiveLimit)
                .ToList();

        var lines = page
            .Select
            (
                e => string.Create
                (
                    CultureInfo.InvariantCulture,
                    $"[{e.Id}] {e.Item.Timestamp:o} [{(e.Item.Stream == LogStream.StdOut ? "stdout" : "stderr")}] {e.Item.Content}"
                )
            )
                .ToList();

        var (content, summary) = McpResponseFormatter.ApplyTokenBudget(lines, lines.Count);

        var header = string.Create
        (
            CultureInfo.InvariantCulture,
            $"Logs for '{slug}'. Total buffered: {buffer.Count}. {summary}"
        );

        return McpResponseFormatter.Success($"{header}\n{content}");
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
