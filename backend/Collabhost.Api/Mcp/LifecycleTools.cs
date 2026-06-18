using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
// Card #332: every tool takes an optional `authKey` per-call argument. Resolution happens
// at the top of each body via McpRequestAuthenticator.
[McpServerToolType]
public class LifecycleTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    ProxyManager proxy,
    ProbeService probeService,
    RuntimeConfigFileWriter runtimeConfigFileWriter,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore,
    RestartAppOperation restartAppOperation,
    KillAppOperation killAppOperation,
    McpRequestAuthenticator authenticator,
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

    // Card #366: mirror REST AppLifecycleEndpoints.StartAppAsync's RunProbesAsync call so
    // MCP start_app refreshes probe-derived metadata (surfaced via get_app) on
    // both the routing-only and process-bearing branches. The original #336/#332
    // MCP path took no ProbeService dep at all -- the REST path called probes on
    // both start branches; the MCP path called probes on neither.
    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    // Card #365: mirror REST AppLifecycleEndpoints.StartAppAsync's routing-only branch so
    // MCP start_app fires the runtime-config-file writer before EnableRoute. The
    // original #336 commit added the writer call to REST but not to MCP -- the
    // MCP path was structurally never wired to the writer.
    private readonly RuntimeConfigFileWriter _runtimeConfigFileWriter = runtimeConfigFileWriter
        ?? throw new ArgumentNullException(nameof(runtimeConfigFileWriter));

    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    // The migrated restart/kill operations injected directly (code-structure-conventions §8: no
    // dispatcher). restart_app / kill_app adapt their slug into the command, call the operation,
    // and map the result; start_app / stop_app / get_logs keep their full bodies and migrate later.
    private readonly RestartAppOperation _restartAppOperation = restartAppOperation
        ?? throw new ArgumentNullException(nameof(restartAppOperation));

    private readonly KillAppOperation _killAppOperation = killAppOperation
        ?? throw new ArgumentNullException(nameof(killAppOperation));

    private readonly McpRequestAuthenticator _authenticator = authenticator
        ?? throw new ArgumentNullException(nameof(authenticator));

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
    [Description("Starts a registered application. For process-based apps (dotnet-app, nodejs-app, executable, system-service, internal-service), this spawns the process; for types that bind port-injection it also allocates a port. For static sites, this enables the Caddy proxy route (no process is involved). For external-route, this enables the Caddy proxy route to the operator-declared upstream (no process is involved). The app must be in 'stopped' or 'crashed' status. Returns immediately after initiating the start. The process may take a few seconds to reach 'running' status. Use get_app to check the current status. Starting an already-running app is a safe no-op.")]
    public async Task<CallToolResult> StartAppAsync
    (
        [Description("The app's unique slug identifier. Use list_apps to find available slugs.")] string slug,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "start_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

        // Routing-only apps (e.g. static sites): enable route instead of starting a process
        if (!hasProcess && hasRouting)
        {
            // Render runtime-config-file BEFORE enabling the route (Card #336).
            // Ordering matters: if we enabled the route first, Caddy could serve a
            // stale on-disk value for an arbitrary window before the writer landed
            // the new file. Mirrors AppLifecycleEndpoints.StartAppAsync's REST routing-only
            // branch -- the original #336 commit added the writer call to REST but
            // never to MCP, leaving the MCP trigger structurally absent. Card #365.
            try
            {
                await _runtimeConfigFileWriter.RenderAsync(app, ct);
            }
            catch (RuntimeConfigFileWriteException ex)
            {
                return McpResponseFormatter.InvalidParameters(ex.Message);
            }

            _proxy.EnableRoute(app.Slug);
            _proxy.RequestSync();

            // Clear the persisted operator-stop flag (REST+MCP parity with
            // AppLifecycleEndpoints.StartAppAsync). Card #350.
            await _appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, false, ct);

            // Refresh probe-derived metadata so get_app reflects the current
            // artifact state. Mirrors AppLifecycleEndpoints.StartAppAsync's routing-only
            // branch; the MCP path took no ProbeService dep before Card #366.
            await _probeService.RunProbesAsync(app.Id, ct);

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

            // Refresh probe-derived metadata (REST+MCP parity). Card #366.
            await _probeService.RunProbesAsync(app.Id, ct);

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
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "stop_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");
        var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

        // Routing-only apps (e.g. static sites): disable route instead of stopping a process
        if (!hasProcess && hasRouting)
        {
            _proxy.DisableRoute(app.Slug);
            _proxy.RequestSync();

            // Persist the operator-stop intent (REST+MCP parity with
            // AppLifecycleEndpoints.StopAppAsync). Card #350.
            await _appStore.SetStoppedByOperatorAsync(app.Id, app.Slug, true, ct);

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
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "restart_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        // The "only process-based apps support restart" pre-check is an MCP-surface Validation
        // guard kept above the operation call (REST has no such guard -- it lets the supervisor
        // throw -> 409), so this MCP-specific message + short-circuit is byte-preserved. It loads
        // the app to read its app-type; the operation re-loads inside the same request scope. The
        // not-found path returns the same AppNotFound the pre-migration body returned.
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        if (!_typeStore.HasBinding(app.AppTypeSlug, "process"))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Cannot restart app '{slug}': only process-based apps support restart. '{slug}' is a {app.AppTypeSlug}."
            );
        }

        var result = await _restartAppOperation.ExecuteAsync(new RestartAppCommand(slug), ct);

        return result.ToCallToolResult();
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
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "kill_app", ct);

        if (authError is not null)
        {
            return authError;
        }

        // MCP-surface Validation guard, as for restart_app: REST has none; this preserves the
        // MCP-specific "only process-based apps support kill" message + short-circuit.
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        if (!_typeStore.HasBinding(app.AppTypeSlug, "process"))
        {
            return McpResponseFormatter.InvalidParameters
            (
                $"Cannot kill app '{slug}': only process-based apps support kill. '{slug}' is a {app.AppTypeSlug}."
            );
        }

        var result = await _killAppOperation.ExecuteAsync(new KillAppCommand(slug), ct);

        return result.ToCallToolResult();
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
        // Explicit `= null` defaults on optional params are load-bearing: Microsoft.Extensions.AI.Abstractions'
        // tool-binding marshaller treats parameters with no C# default as REQUIRED (ParameterInfo.HasDefaultValue
        // is false for `int?`/`string?` without an explicit `= null`). Without these, a client call that omits
        // the argument throws ArgumentException through the marshaller, which ModelContextProtocol.Core then
        // masks to a generic "An error occurred invoking '<tool>'." -- Card #331.
        [Description("Maximum number of log entries to return (1-500). Defaults to 100.")] int? limit = null,
        [Description("Entries to skip from the start of the buffer for pagination. Defaults to 0.")] int? offset = null,
        [Description(McpAuthDescriptions.AuthKey)] string? authKey = null,
        CancellationToken ct = default
    )
    {
        var authError = await _authenticator.AuthenticateAsync(authKey, "get_logs", ct);

        if (authError is not null)
        {
            return authError;
        }

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

// File-scoped mapping from the surface-agnostic operation outcome back to the MCP result shape
// (§7: the surface holds only its file-scoped mapping). The MCP half of the outcome-mapping
// template PRs 3-7 copy. K-1 (Kai's PR-1 forward note): OperationResult.FailureKind defaults to
// ordinal-0 NotFound on a success, so the success arm is gated on IsSuccess FIRST -- FailureKind
// is only read on the failure path. The success shape is the exact { slug, status, appType }
// object the pre-migration body serialized; every failure kind maps to InvalidParameters, the
// single MCP error shape both restart_app and kill_app returned before (the supervisor's
// InvalidOperationException -> Conflict -> InvalidParameters, byte-identical to the old catch).
// The MCP-specific not-found (AppNotFound) and the "only process-based apps support ..." Validation
// guard are handled at the tool body above this mapping, so NotFound/Validation cannot reach here
// on the normal path for restart/kill.
file static class LifecycleOperationResultMapping
{
    public static CallToolResult ToCallToolResult(this OperationResult<AppActionOutcome> result)
    {
        if (result.IsSuccess)
        {
            var outcome = result.Value!;

            return McpResponseFormatter.Success
            (
                McpResponseFormatter.ToJson
                (
                    new { slug = outcome.Slug, status = outcome.State.ToApiString(), appType = outcome.AppTypeSlug }
                )
            );
        }

        // Every failure kind -> InvalidParameters, the single error shape restart_app / kill_app
        // returned before. For these two operations the only kind that reaches here is Conflict
        // (the supervisor's InvalidOperationException, formerly the per-tool catch -> InvalidParameters
        // -- byte-identical). NotFound and Validation are short-circuited by the tool body's pre-load
        // and process-type guard above this mapping; should one reach here on a load race, surfacing
        // the operation's own message verbatim is the right defensive shape (never re-wrapped).
        return McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty);
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
