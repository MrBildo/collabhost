using System.Text.Json.Nodes;

using Collabhost.Api.Services.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class ProxyArtifactTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task FileServerRoute_UsesArtifactLocation_AsRoot()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var tempDir = CreateTempDirectory();
        var externalId = await CreateAppAsync(client, "proxy-artifact-root", staticSite: true, artifactLocation: tempDir);

        // Start the app so the route is enabled (routes are disabled on creation)
        await client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Act
        await client.PostAsync("/api/v1/proxy/reload", null);

        // Assert
        var fake = _fixture.Services.GetRequiredService<IProxyConfigClient>() as FakeProxyConfigClient;
        fake.ShouldNotBeNull();
        fake.LastPushedConfig.ShouldNotBeNull();

        var routes = fake.LastPushedConfig["apps"]?["http"]?["servers"]?["srv0"]?["routes"] as JsonArray;
        routes.ShouldNotBeNull();

        JsonObject? appRoute = null;
        foreach (var route in routes)
        {
            if (route is JsonObject obj && obj["@id"]?.GetValue<string>() == "route_proxy-artifact-root")
            {
                appRoute = obj;
                break;
            }
        }

        appRoute.ShouldNotBeNull("Expected a route with @id = route_proxy-artifact-root");

        // The subroute's first handler should be "vars" with root set to artifact location
        var subroute = appRoute["handle"]?[0]?["routes"] as JsonArray;
        subroute.ShouldNotBeNull();

        var varsHandler = subroute[0]?["handle"]?[0];
        varsHandler.ShouldNotBeNull();
        varsHandler["handler"]?.GetValue<string>().ShouldBe("vars");
        varsHandler["root"]?.GetValue<string>().ShouldBe(tempDir);
    }
}
