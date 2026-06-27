using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Dashboard;

public static class DashboardEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/dashboard").WithTags("Dashboard");

        // Stats are aggregate counts (no secrets, no per-event detail), so they stay open to any
        // authenticated user including the read-only tier. The event feed is the same operational
        // history as /api/v1/events, so it gates to Agent to keep the read-only tier off events.
        group.MapGet("/stats", GetStatsAsync);
        group
            .MapGet("/events", GetEventsAsync)
            .AddEndpointFilter(new RequireRoleFilter(UserRole.Agent));
    }

    private static async Task<IResult> GetStatsAsync
    (
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        // Match the operator-facing picker: internal types like system-service are
        // hidden by /api/v1/app-types so the dashboard tile reflects the same set.
        var appTypes = typeStore.ListTypes()
            .Where(t => !t.IsInternal)
                .ToList();

        var running = 0;
        var stopped = 0;
        var crashed = 0;
        var backoff = 0;
        var fatal = 0;
        string? lastIssuedSlug = null;

        foreach (var app in apps)
        {
            var process = supervisor.GetProcess(app.Id);
            var hasProcess = typeStore.HasBinding(app.AppTypeSlug, "process");
            var hasRouting = typeStore.HasBinding(app.AppTypeSlug, "routing");
            var routeEnabled = hasRouting && proxy.IsRouteEnabled(app.Slug);

            // Use the shared resolver so the dashboard strip and the app list agree on what
            // "running" means.
            var state = AppStatusResolver.Resolve(hasProcess, process, hasRouting, routeEnabled);

            switch (state)
            {
                case ProcessState.Running:
                    running++;
                    break;
                case ProcessState.Crashed:
                    crashed++;
                    lastIssuedSlug = app.Slug;
                    break;
                case ProcessState.Backoff:
                    backoff++;
                    lastIssuedSlug ??= app.Slug;
                    break;
                case ProcessState.Fatal:
                    fatal++;
                    lastIssuedSlug = app.Slug;
                    break;
                case ProcessState.Stopped:
                case ProcessState.Starting:
                case ProcessState.Stopping:
                case ProcessState.Restarting:
                    stopped++;
                    break;
            }
        }

        var issues = crashed + backoff + fatal;

        var issuesSummary = lastIssuedSlug is not null
            ? $"{lastIssuedSlug} {(fatal > 0 ? "fatal" : crashed > 0 ? "crashed" : "backoff")}"
            : null;

        var stats = new DashboardStats
        (
            apps.Count,
            running,
            stopped,
            crashed,
            backoff,
            fatal,
            issues,
            issuesSummary,
            appTypes.Count
        );

        return TypedResults.Ok(stats);
    }

    private static async Task<IResult> GetEventsAsync
    (
        int? limit,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        var events = await activityEventStore.GetRecentAsync(
            Math.Min(limit ?? 20, 100), ct);

        var items = events.Select(e => new DashboardEventResponse
        (
            Id: e.Id.ToCanonicalString(),
            Timestamp: e.Timestamp,
            Message: FormatEventMessage(e),
            AppSlug: e.AppSlug,
            Source: e.ActorName,
            Severity: ActivityEventStore.DeriveSeverity(e.EventType)
        ));

        return TypedResults.Ok(new { events = items });
    }

    private static string FormatEventMessage(ActivityEvent e)
    {
        // Metadata is parsed lazily — only when needed for message formatting
        JsonDocument? doc = null;

        try
        {
            if (e.MetadataJson is not null)
            {
                doc = JsonDocument.Parse(e.MetadataJson);
            }

            return e.EventType switch
            {
                ActivityEventTypes.AppStarted => "started",
                ActivityEventTypes.AppStopped => "stopped",
                ActivityEventTypes.AppRestarted => "restarted",
                ActivityEventTypes.AppKilled => "killed",
                ActivityEventTypes.AppCreated => "created",
                ActivityEventTypes.AppDeleted => "deleted",
                ActivityEventTypes.AppCrashed when TryGetInt(doc, "exitCode", out var exitCode)
                    => $"crashed (exit code {exitCode.ToString(CultureInfo.InvariantCulture)})",
                ActivityEventTypes.AppCrashed => "crashed",
                ActivityEventTypes.AppFatal => "fatal (max restarts exceeded)",
                ActivityEventTypes.AppAutoStarted => "auto-started",
                ActivityEventTypes.AppAutoRestarted when TryGetInt(doc, "restartCount", out var restartCount)
                    => $"auto-restarted (attempt {restartCount.ToString(CultureInfo.InvariantCulture)})",
                ActivityEventTypes.AppAutoRestarted => "auto-restarted",
                ActivityEventTypes.AppSeeded => "seeded",
                ActivityEventTypes.AppSettingsUpdated when TryGetStringArray(doc, "changedCapabilities", out var caps)
                    => $"settings updated ({string.Join(", ", caps)})",
                ActivityEventTypes.AppSettingsUpdated => "settings updated",
                ActivityEventTypes.ProxyReloaded => "proxy config reloaded",
                ActivityEventTypes.UserCreated when TryGetString(doc, "targetName", out var name)
                    => $"created user {name}",
                ActivityEventTypes.UserCreated => "created user",
                ActivityEventTypes.UserDeactivated when TryGetString(doc, "targetName", out var name)
                    => $"deactivated user {name}",
                ActivityEventTypes.UserDeactivated => "deactivated user",
                ActivityEventTypes.UserSeeded => "admin user seeded",
                _ => e.EventType
            };
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static bool TryGetInt(JsonDocument? doc, string key, out int value)
    {
        value = 0;

        return doc is not null
            && doc.RootElement.TryGetProperty(key, out var prop)
            && prop.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonDocument? doc, string key, out string value)
    {
        value = string.Empty;

        if (doc is null || !doc.RootElement.TryGetProperty(key, out var prop))
        {
            return false;
        }

        value = prop.GetString() ?? string.Empty;

        return value.Length > 0;
    }

    private static bool TryGetStringArray(JsonDocument? doc, string key, out string[] value)
    {
        value = [];

        if (doc is null || !doc.RootElement.TryGetProperty(key, out var prop))
        {
            return false;
        }

        if (prop.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        value =
        [
            .. prop.EnumerateArray()
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => s.Length > 0)
        ];

        return value.Length > 0;
    }
}
