using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Mcp;
using Collabhost.Api.Platform;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Cross-surface route-target parity (Card #435).
//
// The property under guard: the 4 surfaces that render an app's upstream-target string --
// REST App Detail (route.target), REST Routes table (the entry's target), MCP list_routes
// (the route's target), MCP get_app (routeTarget) -- ALL produce the IDENTICAL string for
// the same app. Before the dedup the synthesis was copied near-verbatim 4 times and had
// already drifted twice: MCP get_app fell through to a not-running label for a healthy
// external route (MCP-04), and the Routes table rendered the file-server label as Static
// Files while the other three rendered it as file-server. The shared RouteTargetResolver
// is what makes these agree; this suite goes RED if a future change reintroduces a
// per-surface copy that drifts.
//
// It drives the LIVE surfaces, not the resolver in isolation: the two REST reads go over
// HTTP through the real endpoints, and the two MCP reads call the real tool classes via DI
// -- the MCP HTTP transport is SSE and incompatible with TestHost buffering, the same
// approach the MCP tool tests take. The resolver branches are covered in isolation by the
// resolver unit tests; this suite proves the four call sites actually route through it.
[Collection("Api")]
public class RouteTargetParityTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;
    private readonly IServiceProvider _services = fixture.Services;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExternalRoute_AllFourSurfaces_ReportIdenticalTarget()
    {
        var slug = await CreateExternalRouteAppAsync("192.168.1.50", 11235, "http");

        try
        {
            var targets = await CollectTargetsAsync(slug);

            // The whole point: one string, everywhere.
            targets.Distinct().Count().ShouldBe
            (
                1,
                $"Route-target surfaces disagreed for '{slug}': "
                + $"restDetail='{targets.RestDetail}', restRoutes='{targets.RestRoutes}', "
                + $"mcpListRoutes='{targets.McpListRoutes}', mcpGetApp='{targets.McpGetApp}'."
            );

            targets.RestDetail.ShouldBe("http://192.168.1.50:11235");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    [Fact]
    public async Task FileServer_AllFourSurfaces_ReportIdenticalTarget()
    {
        // The convergence case (operator ruling B): a static-site is a file-server route. App
        // Detail + both MCP tools previously emitted the raw Caddy handler name "file-server"
        // while the Routes table already used "Static Files". Every surface must now report
        // "Static Files" identically (the three Caddy-leaking surfaces changed to match the
        // vendor-abstracted label the Routes table already had).
        var slug = await CreateStaticSiteAppAsync();

        try
        {
            var targets = await CollectTargetsAsync(slug);

            targets.Distinct().Count().ShouldBe
            (
                1,
                $"File-server target surfaces disagreed for '{slug}': "
                + $"restDetail='{targets.RestDetail}', restRoutes='{targets.RestRoutes}', "
                + $"mcpListRoutes='{targets.McpListRoutes}', mcpGetApp='{targets.McpGetApp}'."
            );

            targets.RestRoutes.ShouldBe("Static Files");
        }
        finally
        {
            await DeleteAsync(slug);
        }
    }

    private async Task<RouteTargets> CollectTargetsAsync(string slug)
    {
        var restDetail = await ReadRestDetailTargetAsync(slug);
        var restRoutes = await ReadRestRoutesTargetAsync(slug);
        var mcpListRoutes = await ReadMcpListRoutesTargetAsync(slug);
        var mcpGetApp = await ReadMcpGetAppTargetAsync(slug);

        return new RouteTargets(restDetail, restRoutes, mcpListRoutes, mcpGetApp);
    }

    private async Task<string?> ReadRestDetailTargetAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return detail.GetProperty("route").GetProperty("target").GetString();
    }

    private async Task<string?> ReadRestRoutesTargetAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        foreach (var entry in body.GetProperty("routes").EnumerateArray())
        {
            if (entry.GetProperty("appName").GetString() == slug)
            {
                return entry.GetProperty("target").GetString();
            }
        }

        throw new InvalidOperationException($"App '{slug}' not found in REST /routes response.");
    }

    private async Task<string?> ReadMcpListRoutesTargetAsync(string slug)
    {
        await using var scope = _services.CreateAsyncScope();
        var tools = CreateConfigurationTools(scope.ServiceProvider);

        var result = await tools.ListRoutesAsync(authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();

        var json = JsonDocument.Parse(GetFirstText(result)).RootElement;

        foreach (var route in json.GetProperty("routes").EnumerateArray())
        {
            if (route.GetProperty("slug").GetString() == slug)
            {
                return route.GetProperty("target").GetString();
            }
        }

        throw new InvalidOperationException($"App '{slug}' not found in MCP list_routes response.");
    }

    private async Task<string?> ReadMcpGetAppTargetAsync(string slug)
    {
        await using var scope = _services.CreateAsyncScope();
        var tools = CreateDiscoveryTools(scope.ServiceProvider);

        var result = await tools.GetAppAsync(slug, authKey: ApiFixture.AdminKey, CancellationToken.None);

        (result.IsError ?? false).ShouldBeFalse();

        var json = JsonDocument.Parse(GetFirstText(result)).RootElement;

        return json.GetProperty("routeTarget").GetString();
    }

    private static ConfigurationTools CreateConfigurationTools(IServiceProvider sp) =>
        new
        (
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<ProxyManager>(),
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<ReloadProxyOperation>(),
            sp.GetRequiredService<UpdateSettingsOperation>(),
            sp.GetRequiredService<McpRequestAuthenticator>()
        );

    private static DiscoveryTools CreateDiscoveryTools(IServiceProvider sp) =>
        new
        (
            sp.GetRequiredService<IApplicationStartTime>(),
            sp.GetRequiredService<AppStore>(),
            sp.GetRequiredService<TypeStore>(),
            sp.GetRequiredService<ProcessSupervisor>(),
            sp.GetRequiredService<ProxyManager>(),
            sp.GetRequiredService<ProxySettings>(),
            sp.GetRequiredService<ProbeService>(),
            sp.GetRequiredService<AppDataPathResolver>(),
            sp.GetRequiredService<McpRequestAuthenticator>()
        );

    private async Task<string> CreateExternalRouteAppAsync(string host, int port, string scheme)
    {
        var slug = $"parity-ext-{Guid.NewGuid().ToString("N")[..8]}";

        var payload = new
        {
            name = slug,
            displayName = "Parity External Route",
            appTypeSlug = "external-route",
            values = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal)
            {
                ["external-target"] = new(StringComparer.Ordinal)
                {
                    ["host"] = host,
                    ["port"] = port,
                    ["scheme"] = scheme
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return slug;
    }

    private async Task<string> CreateStaticSiteAppAsync()
    {
        var slug = $"parity-static-{Guid.NewGuid().ToString("N")[..8]}";

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create
        (
            new { name = slug, displayName = "Parity Static Site", appTypeSlug = "static-site" },
            options: _jsonOptions
        );

        var response = await _client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return slug;
    }

    private async Task DeleteAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(request);
    }

    private static string GetFirstText(CallToolResult result) =>
        result.Content is { Count: > 0 } && result.Content[0] is TextContentBlock textBlock
            ? textBlock.Text
            : string.Empty;

    // The four surfaces' rendered target strings for one app, in one shape so the parity
    // assertion is a single Distinct().Count() == 1.
    private sealed record RouteTargets(string? RestDetail, string? RestRoutes, string? McpListRoutes, string? McpGetApp)
    {
        public IEnumerable<string?> Distinct() =>
            new[] { RestDetail, RestRoutes, McpListRoutes, McpGetApp }.Distinct(StringComparer.Ordinal);
    }
}
