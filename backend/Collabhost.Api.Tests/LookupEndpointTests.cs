using System.Net;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class LookupEndpointTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetRestartPolicies_ReturnsSeededValues()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/restart-policies");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBe(3);

        var names = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        names.ShouldContain(StringCatalog.RestartPolicies.Never);
        names.ShouldContain(StringCatalog.RestartPolicies.OnCrash);
        names.ShouldContain(StringCatalog.RestartPolicies.Always);
    }

    [Fact]
    public async Task GetRestartPolicies_IncludesDisplayNames()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/restart-policies");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var displayNames = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("displayName").GetString())
            .ToList();
        displayNames.ShouldContain("Never");
        displayNames.ShouldContain("On Crash");
        displayNames.ShouldContain("Always");
    }

    [Fact]
    public async Task GetServeModes_ReturnsSeededValues()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/serve-modes");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBe(2);

        var names = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        names.ShouldContain(StringCatalog.ServeModes.ReverseProxy);
        names.ShouldContain(StringCatalog.ServeModes.FileServer);
    }

    [Fact]
    public async Task GetServeModes_IncludesDisplayNames()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/serve-modes");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var displayNames = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("displayName").GetString())
            .ToList();
        displayNames.ShouldContain("Reverse Proxy");
        displayNames.ShouldContain("File Server");
    }

    [Fact]
    public async Task GetDiscoveryStrategies_ReturnsSeededValues()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/discovery-strategies");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBe(3);

        var names = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();
        names.ShouldContain(StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig);
        names.ShouldContain(StringCatalog.DiscoveryStrategies.PackageJson);
        names.ShouldContain(StringCatalog.DiscoveryStrategies.Manual);
    }

    [Fact]
    public async Task GetDiscoveryStrategies_IncludesDisplayNames()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/discovery-strategies");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var displayNames = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("displayName").GetString())
            .ToList();
        displayNames.ShouldContain(".NET Runtime Config");
        displayNames.ShouldContain("package.json");
        displayNames.ShouldContain("Manual");
    }

    [Fact]
    public async Task GetRestartPolicies_ReturnsInOrdinalOrder()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/lookups/restart-policies");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var names = json.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString())
            .ToList();

        names[0].ShouldBe(StringCatalog.RestartPolicies.Never);
        names[1].ShouldBe(StringCatalog.RestartPolicies.OnCrash);
        names[2].ShouldBe(StringCatalog.RestartPolicies.Always);
    }
}
