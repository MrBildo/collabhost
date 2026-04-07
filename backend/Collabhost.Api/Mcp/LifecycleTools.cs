using System.ComponentModel;
using System.Globalization;

using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

[McpServerToolType]
public class LifecycleTools
(
    AppStore appStore,
    ProcessSupervisor supervisor
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

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
