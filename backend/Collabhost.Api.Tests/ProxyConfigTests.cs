using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Services.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class ProxyConfigTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task Config_GeneratesReverseProxyRoute_ForExecutableApp()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "proxy-exec-test");

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var routes = GetRoutes(fake.LastPushedConfig);
        routes.ShouldNotBeNull();

        var appRoute = FindRouteById(routes, "route_proxy-exec-test");
        appRoute.ShouldNotBeNull("Expected a route with @id = route_proxy-exec-test");

        var handler = appRoute["handle"]?[0]?["handler"]?.GetValue<string>();
        handler.ShouldBe("reverse_proxy");
    }

    [Fact]
    public async Task Config_GeneratesFileServerRoute_ForStaticSite()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "proxy-static-test", staticSite: true);

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var routes = GetRoutes(fake.LastPushedConfig);
        routes.ShouldNotBeNull();

        var appRoute = FindRouteById(routes, "route_proxy-static-test");
        appRoute.ShouldNotBeNull("Expected a route with @id = route_proxy-static-test");

        // file_server is wrapped in a subroute handler
        var handler = appRoute["handle"]?[0]?["handler"]?.GetValue<string>();
        handler.ShouldBe("subroute");
    }

    [Fact]
    public async Task Config_IncludesSelfRoute()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var routes = GetRoutes(fake.LastPushedConfig);
        routes.ShouldNotBeNull();

        var selfRoute = FindRouteById(routes, "route_collabhost");
        selfRoute.ShouldNotBeNull("Self-route for collabhost should always be present");

        var handler = selfRoute["handle"]?[0]?["handler"]?.GetValue<string>();
        handler.ShouldBe("reverse_proxy");

        var dial = selfRoute["handle"]?[0]?["upstreams"]?[0]?["dial"]?.GetValue<string>();
        dial.ShouldNotBeNull();
        dial.ShouldContain("localhost:");
    }

    [Fact]
    public async Task Config_IncludesTlsSubjects_ForAllRoutableApps()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "proxy-tls-test");

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var subjects = fake.LastPushedConfig["apps"]?["tls"]?["automation"]
            ?["policies"]?[0]?["subjects"] as JsonArray;
        subjects.ShouldNotBeNull();

        var subjectStrings = subjects.Select(s => s?.GetValue<string>()).ToList();
        subjectStrings.ShouldContain("collabhost.collab.internal");
        subjectStrings.ShouldContain("proxy-tls-test.collab.internal");
    }

    [Fact]
    public async Task Config_UsesRouteIdConvention()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "proxy-id-test");

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var routes = GetRoutes(fake.LastPushedConfig);
        routes.ShouldNotBeNull();

        var appRoute = FindRouteById(routes, "route_proxy-id-test");
        appRoute.ShouldNotBeNull();

        var id = appRoute["@id"]?.GetValue<string>();
        id.ShouldBe("route_proxy-id-test");
    }

    [Fact]
    public async Task GetRoutes_ReturnsAllRoutableApps()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        await CreateAppAsync(client, "proxy-routes-test");

        // Act
        var response = await client.GetAsync("/api/v1/routes");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("baseDomain").GetString().ShouldBe("collab.internal");

        var routes = json.RootElement.GetProperty("routes");
        routes.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        var routeArray = routes.EnumerateArray().ToList();
        var matchingRoute = routeArray.FirstOrDefault
        (
            r => r.GetProperty("domain").GetString()?.Contains("proxy-routes-test", StringComparison.Ordinal) == true
        );

        matchingRoute.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        matchingRoute.GetProperty("https").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Reload_TriggersConfigSync()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();

        var countBefore = fake.LoadCallCount;

        // Act
        var response = await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("success").GetBoolean().ShouldBeTrue();

        fake.LoadCallCount.ShouldBeGreaterThan(countBefore);
    }

    [Fact]
    public async Task GetProxyStatus_ReturnsExpectedShape()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/proxy/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("state").GetString().ShouldNotBeNullOrWhiteSpace();
        json.RootElement.GetProperty("adminApiReady").GetBoolean().ShouldBeTrue();
        json.RootElement.GetProperty("routeCount").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        json.RootElement.GetProperty("baseDomain").GetString().ShouldBe("collab.internal");
    }

    private static JsonArray? GetRoutes(JsonObject config) =>
        config["apps"]?["http"]?["servers"]?["srv0"]?["routes"] as JsonArray;

    private static JsonObject? FindRouteById(JsonArray routes, string id)
    {
        foreach (var route in routes)
        {
            if (route is JsonObject obj && obj["@id"]?.GetValue<string>() == id)
            {
                return obj;
            }
        }

        return null;
    }

    private static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    private static object CreateValidRequest(string name, bool staticSite = false)
    {
        var appTypeId = staticSite
            ? IdentifierCatalog.AppTypes.StaticSite
            : IdentifierCatalog.AppTypes.Executable;

        return new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = appTypeId,
            InstallDirectory = $"C:\\apps\\{name}"
        };
    }

    private static async Task<string> CreateAppAsync
    (
        HttpClient client,
        string name,
        bool staticSite = false
    )
    {
        var request = CreateValidRequest(name, staticSite);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
