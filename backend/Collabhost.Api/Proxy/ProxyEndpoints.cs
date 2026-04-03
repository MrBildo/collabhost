using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0051 // Long method justified -- route projection with capability resolution
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
        ProcessSupervisor supervisor,
        ProxySettings settings,
        CancellationToken ct
    )
    {
        var apps = await store.ListAsync(ct);

        var entries = new List<RouteListEntry>();

        foreach (var app in apps)
        {
            var bindings = await store.GetBindingsAsync(app.AppTypeId, ct);

            var routingBinding = bindings.FirstOrDefault
            (
                b => string.Equals(b.CapabilitySlug, "routing", StringComparison.Ordinal)
            );

            if (routingBinding is null)
            {
                continue;
            }

            var overrides = await store.GetOverridesAsync(app.Id, ct);

            var overrideJson = overrides.TryGetValue("routing", out var routingOverride)
                ? routingOverride.ConfigurationJson
                : null;

            var routingConfiguration = CapabilityResolver.Resolve<RoutingConfiguration>
            (
                routingBinding.DefaultConfigurationJson, overrideJson
            );

            var domain = routingConfiguration.DomainPattern
                .Replace("{slug}", app.Slug, StringComparison.OrdinalIgnoreCase);

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
                    AppExternalId: app.Id.ToString(),
                    AppName: app.Slug,
                    AppDisplayName: app.DisplayName,
                    Domain: domain,
                    Target: target,
                    ProxyMode: routingConfiguration.ServeMode == ServeMode.ReverseProxy
                        ? "reverseProxy"
                        : "fileServer",
                    Https: true
                )
            );
        }

        return TypedResults.Ok
        (
            new RouteListResponse(entries, settings.BaseDomain)
        );
    }

    private static Task<IResult> ReloadProxyAsync(ProxyManager proxy)
    {
        proxy.RequestSync();

        return Task.FromResult<IResult>(TypedResults.NoContent());
    }
}
#pragma warning restore MA0051
#pragma warning restore MA0011
#pragma warning restore MA0076
