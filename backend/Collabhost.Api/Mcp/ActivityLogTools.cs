using System.ComponentModel;
using System.Globalization;
using System.Text;

using Collabhost.Api.ActivityLog;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

[McpServerToolType]
public class ActivityLogTools(ActivityEventStore activityEventStore)
{
    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    // MCP tool caps at 50 events while the REST endpoint caps at 200.
    // This is intentional: MCP responses are consumed as LLM context tokens,
    // where every line counts. REST serves UIs that render large lists efficiently.
    private const int _defaultLimit = 20;
    private const int _maxLimit = 50;

    [McpServerTool
    (
        Name = "list_events",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false
    )]
    [Description("Lists recent activity events (state changes, operator actions, system events). Use to understand what happened to an app or who performed an action. Filter by app slug, event type, or category. Returns newest events first.")]
    public async Task<CallToolResult> ListEventsAsync
    (
        [Description("Filter by app slug (e.g., 'my-api'). Only returns events for this app.")] string? appSlug,
        [Description("Filter by event type (e.g., 'app.crashed', 'app.started'). Exact match.")] string? eventType,
        [Description("Filter by category: 'app', 'user', or 'proxy'. Matches the prefix of event types.")] string? category,
        [Description("Maximum number of events to return (default 20, max 50).")] int? limit,
        CancellationToken ct
    )
    {
        var effectiveLimit = Math.Clamp(limit ?? _defaultLimit, 1, _maxLimit);

        var query = new ActivityEventQuery
        (
            Category: string.IsNullOrEmpty(category) ? null : category,
            AppSlug: string.IsNullOrEmpty(appSlug) ? null : appSlug,
            ActorId: null,
            EventType: string.IsNullOrEmpty(eventType) ? null : eventType,
            Since: null,
            Until: null,
            Limit: effectiveLimit
        );

        var page = await _activityEventStore.QueryAsync(query, ct);

        var header = BuildHeader(page, effectiveLimit, appSlug, eventType, category);

        var lines = new List<string>(page.Items.Count + 2)
        {
            header,
            string.Empty
        };

        foreach (var ev in page.Items)
        {
            lines.Add(FormatEvent(ev));
        }

        return McpResponseFormatter.Success(string.Join("\n", lines));
    }

    private static string BuildHeader
    (
        ActivityEventPage page,
        int effectiveLimit,
        string? appSlug,
        string? eventType,
        string? category
    )
    {
        var hasFilter = !string.IsNullOrEmpty(appSlug)
            || !string.IsNullOrEmpty(eventType)
            || !string.IsNullOrEmpty(category);

        var count = page.Items.Count;

        return hasFilter
            ? page.HasMore
                ? string.Create(CultureInfo.InvariantCulture, $"Showing {count} events (newest first, more available). Use appSlug, eventType, or category params to filter.")
                : string.Create(CultureInfo.InvariantCulture, $"Showing {count} events matching filter (newest first).")
            : page.HasMore
                ? string.Create(CultureInfo.InvariantCulture, $"Showing {effectiveLimit} events (newest first, more available). Use appSlug, eventType, or category params to filter.")
                : string.Create(CultureInfo.InvariantCulture, $"Showing {count} events (newest first). Use appSlug, eventType, or category params to filter.");
    }

    private static string FormatEvent(ActivityEvent ev)
    {
        var timestamp = ev.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();

        sb.Append('[');
        sb.Append(timestamp);
        sb.Append("] ");
        sb.Append(ev.EventType);

        if (ev.AppSlug is not null)
        {
            sb.Append(' ');
            sb.Append(ev.AppSlug);
        }

        var metadata = ExtractMetadata(ev);

        if (metadata is not null)
        {
            sb.Append(' ');
            sb.Append(metadata);
        }

        var actor = string.Equals(ev.ActorId, ActivityActor.SystemId, StringComparison.Ordinal)
            ? "(system)"
            : string.Create(CultureInfo.InvariantCulture, $"(by {ev.ActorName})");

        sb.Append(' ');
        sb.Append(actor);

        return sb.ToString();
    }

    private static string? ExtractMetadata(ActivityEvent ev)
    {
        if (ev.MetadataJson is null)
        {
            return null;
        }

        try
        {
            var doc = JsonDocument.Parse(ev.MetadataJson);
            var root = doc.RootElement;

            return ev.EventType switch
            {
                ActivityEventTypes.AppCrashed => TryGetInt(root, "exitCode") is { } exitCode
                    ? string.Create(CultureInfo.InvariantCulture, $"exit={exitCode}")
                    : null,

                ActivityEventTypes.AppFatal => TryGetInt(root, "failureCount") is { } failureCount
                    ? string.Create(CultureInfo.InvariantCulture, $"failures={failureCount}")
                    : null,

                ActivityEventTypes.AppAutoRestarted =>
                    FormatAutoRestarted(root),

                ActivityEventTypes.AppSettingsUpdated => TryGetStringArray(root, "changedCapabilities") is { } caps
                    ? $"changed={string.Join(",", caps)}"
                    : null,

                ActivityEventTypes.AppCreated => TryGetString(root, "appTypeSlug") is { } typeSlug
                    ? $"type={typeSlug}"
                    : null,

                ActivityEventTypes.AppDeleted => TryGetString(root, "displayName") is { } displayName
                    ? $"was={displayName}"
                    : null,

                ActivityEventTypes.UserCreated => FormatUserEvent(root),

                ActivityEventTypes.UserDeactivated => TryGetString(root, "targetName") is { } targetName
                    ? $"target={targetName}"
                    : null,

                ActivityEventTypes.UserSeeded => TryGetString(root, "role") is { } role
                    ? $"role={role}"
                    : null,

                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FormatAutoRestarted(JsonElement root)
    {
        var parts = new List<string>(2);

        if (TryGetInt(root, "restartCount") is { } restartCount)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"restart={restartCount}"));
        }

        if (TryGetInt(root, "exitCode") is { } exitCode)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"exit={exitCode}"));
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? FormatUserEvent(JsonElement root)
    {
        var parts = new List<string>(2);

        if (TryGetString(root, "targetName") is { } targetName)
        {
            parts.Add($"target={targetName}");
        }

        if (TryGetString(root, "role") is { } role)
        {
            parts.Add($"role={role}");
        }

        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? TryGetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? TryGetInt(JsonElement root, string key) =>
        root.TryGetProperty(key, out var prop) && prop.TryGetInt32(out var value)
            ? value
            : null;

    private static List<string>? TryGetStringArray(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var items = new List<string>();

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
            {
                items.Add(s);
            }
        }

        return items.Count > 0 ? items : null;
    }
}
