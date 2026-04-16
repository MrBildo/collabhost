using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
public static class ProxyEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("Proxy");

        group.MapGet("/routes", ListRoutesAsync);
        group.MapPost("/proxy/reload", ReloadProxyAsync);
    }

    private static async Task<IResult> ListRoutesAsync
    (
        AppStore store,
        TypeStore typeStore,
        ProcessSupervisor supervisor,
        ProxyManager proxy,
        ProxySettings settings,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        var entries = new List<RouteListEntry>();

        foreach (var app in apps)
        {
            var bindings = typeStore.GetBindings(app.AppTypeSlug);

            if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
            {
                continue;
            }

            var overrides = await store.GetOverridesAsync(app.Id, ct);

            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            var routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBindingJson, overrideJson
            );

            var domain = CapabilityResolver.ResolveDomain
            (
                routingConfiguration.DomainPattern, app.Slug, settings.BaseDomain
            );

            var process = supervisor.GetProcess(app.Id);

            var target = routingConfiguration.ServeMode == ServeMode.ReverseProxy
                ? process?.Port is not null
                    ? $"localhost:{process.Port.Value.ToString(CultureInfo.InvariantCulture)}"
                    : "not-running"
                : "file-server";

            entries.Add
            (
                new RouteListEntry
                (
                    app.Id.ToString(),
                    app.Slug,
                    app.DisplayName,
                    domain,
                    target,
                    routingConfiguration.ServeMode == ServeMode.ReverseProxy
                        ? "reverseProxy"
                        : "fileServer",
                    true,
                    proxy.IsRouteEnabled(app.Slug)
                )
            );
        }

        return TypedResults.Ok
        (
            new RouteListResponse(entries, settings.BaseDomain)
        );
    }

    private static async Task<IResult> ReloadProxyAsync
    (
        ProxyManager proxy,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        proxy.RequestSync();

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.ProxyReloaded,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = null,
                AppSlug = null,
                MetadataJson = null
            },
            ct
        );

        return TypedResults.NoContent();
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
