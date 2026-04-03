using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Dashboard;

public static class DashboardEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/dashboard").WithTags("Dashboard");

        group.MapGet("/stats", GetStatsAsync);
    }

    private static async Task<IResult> GetStatsAsync
    (
        AppStore store,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);
        var appTypes = await store.ListAppTypesAsync(ct);

        var running = 0;
        var stopped = 0;
        var crashed = 0;
        string? lastCrashedSlug = null;

        foreach (var app in apps)
        {
            var process = supervisor.GetProcess(app.Id);
            var hasProcess = await store.HasBindingAsync(app.AppTypeId, "process", ct);
            var hasRouting = await store.HasBindingAsync(app.AppTypeId, "routing", ct);

            if (!hasProcess && !hasRouting)
            {
                stopped++;
                continue;
            }

            ProcessState state;

            if (hasProcess)
            {
                state = process?.State ?? ProcessState.Stopped;
            }
            else
            {
                // Routing-only: route enabled = running
                state = proxy.IsRouteEnabled(app.Slug) ? ProcessState.Running : ProcessState.Stopped;
            }

            switch (state)
            {
                case ProcessState.Running:
                    running++;
                    break;
                case ProcessState.Crashed:
                    crashed++;
                    lastCrashedSlug = app.Slug;
                    break;
                default:
                    stopped++;
                    break;
            }
        }

        var issuesSummary = lastCrashedSlug is not null
            ? $"{lastCrashedSlug} crashed"
            : null;

        var stats = new DashboardStats
        (
            TotalApps: apps.Count,
            Running: running,
            Stopped: stopped,
            Crashed: crashed,
            Issues: crashed,
            IssuesSummary: issuesSummary,
            UptimePercent24h: null,
            IncidentsThisWeek: 0,
            MemoryUsedMb: null,
            MemoryTotalMb: null,
            RequestsPerMinute: null,
            AppTypes: appTypes.Count
        );

        return TypedResults.Ok(stats);
    }
}
