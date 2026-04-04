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
        var backoff = 0;
        var fatal = 0;
        string? lastIssuedSlug = null;

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
            null,
            0,
            null,
            null,
            null,
            appTypes.Count
        );

        return TypedResults.Ok(stats);
    }
}
