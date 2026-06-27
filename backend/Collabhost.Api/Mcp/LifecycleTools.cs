using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

// Card #332: every tool takes an optional `authKey` per-call argument. Resolution happens
// at the top of each body via McpRequestAuthenticator.
[McpServerToolType]
public class LifecycleTools
(
    AppStore appStore,
    TypeStore typeStore,
    ProcessSupervisor supervisor,
    StartAppOperation startAppOperation,
    StopAppOperation stopAppOperation,
    RestartAppOperation restartAppOperation,
    KillAppOperation killAppOperation,
    McpRequestAuthenticator authenticator
)
{
    // The tool keeps three direct deps after the lifecycle migration: AppStore for the
    // MCP-surface not-found pre-check (the AppNotFound shape, kept above the operation), TypeStore
    // for the restart/kill MCP-surface "only process-based apps support ..." guard (kept above the
    // operation, since REST has no such guard), and ProcessSupervisor for get_logs' ring-buffer
    // read. The proxy / probe / writer / current-user / event-store deps the old start/stop bodies
    // held now live inside StartAppOperation / StopAppOperation, so they leave this ctor.
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    // The migrated lifecycle operations injected directly (code-structure-conventions §8: no
    // dispatcher). Each tool adapts its slug into the command, calls the operation, and maps the
    // result; get_logs is the one remaining body (a read of the supervisor's ring buffer, not a
    // mutating operation, so it never joins the spine).
    private readonly StartAppOperation _startAppOperation = startAppOperation
        ?? throw new ArgumentNullException(nameof(startAppOperation));

    private readonly StopAppOperation _stopAppOperation = stopAppOperation
        ?? throw new ArgumentNullException(nameof(stopAppOperation));

    private readonly RestartAppOperation _restartAppOperation = restartAppOperation
        ?? throw new ArgumentNullException(nameof(restartAppOperation));

    private readonly KillAppOperation _killAppOperation = killAppOperation
        ?? throw new ArgumentNullException(nameof(killAppOperation));

    private readonly McpRequestAuthenticator _authenticator = authenticator
        ?? throw new ArgumentNullException(nameof(authenticator));

    [McpServerTool
    (
        Name = "start_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Starts a registered application. For process-based apps (dotnet-app, nodejs-app, executable, system-service, internal-service), this spawns the process; for types that bind port-injection it also allocates a port. For static sites, this enables the proxy route (no process is involved). For external-route, this enables the proxy route to the operator-declared upstream (no process is involved). The app must be in 'stopped' or 'crashed' status. Returns immediately after initiating the start. The process may take a few seconds to reach 'running' status. Use get_app to check the current status. Starting an already-running app is a safe no-op.")]
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

        // The not-found pre-check is an MCP-surface concern kept above the operation call: MCP
        // returns its own AppNotFound shape (a specific "use list_apps" message), where REST
        // returns an empty 404 -- a genuine surface divergence that stays at the surface, exactly
        // as the restart/kill adapters do. The operation re-loads inside the same request scope and
        // handles the dual-branch (routing-only vs process) body; this adapter only adapts the slug
        // into the command and maps the result back to the MCP shape. Card #365/#366 (writer +
        // probe on the routing-only branch) and Card #350 (persist-flag) now live in the operation.
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var result = await _startAppOperation.ExecuteAsync(new StartAppCommand(slug), ct);

        return result.ToCallToolResult();
    }

    [McpServerTool
    (
        Name = "stop_app",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Stops a running application. For process-based apps, this sends a graceful shutdown signal (CTRL+C / SIGTERM). For static sites, this disables the proxy route. Returns immediately after initiating the stop. Use get_app to check the current status. Stopping an already-stopped app is a safe no-op.")]
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

        // MCP-surface not-found pre-check (the AppNotFound shape REST does not return), as for
        // start_app: the operation owns the dual-branch (routing-only vs process) body and the
        // Card #350 persist-flag; this adapter adapts the slug into the command and maps the result.
        var app = await _appStore.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return McpResponseFormatter.AppNotFound(slug);
        }

        var result = await _stopAppOperation.ExecuteAsync(new StopAppCommand(slug), ct);

        return result.ToCallToolResult();
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
// single MCP error shape all four lifecycle tools (start/stop/restart/kill) returned before for a
// failed action (the supervisor's InvalidOperationException -> Conflict -> InvalidParameters, plus
// Start's runtime-config-file write failure -> Conflict -> InvalidParameters -- both byte-identical
// to the old per-tool catch). The MCP-specific not-found (AppNotFound) is handled by the pre-load
// in each tool body above this mapping, and the "only process-based apps support ..." Validation
// guard by restart/kill's process-type pre-check, so NotFound/Validation cannot reach here on the
// normal path.
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

        // Every failure kind -> InvalidParameters, the single error shape the lifecycle tools
        // returned before. The kinds that reach here are Conflict (the supervisor's
        // InvalidOperationException, or Start's RuntimeConfigFileWriteException -- formerly the
        // per-tool catch -> InvalidParameters, byte-identical). NotFound and Validation are
        // short-circuited by the tool body's pre-load + (restart/kill) process-type guard above this
        // mapping; should one reach here on a load race, surfacing the operation's own message
        // verbatim is the right defensive shape (never re-wrapped).
        return McpResponseFormatter.InvalidParameters(result.Error ?? string.Empty);
    }
}
