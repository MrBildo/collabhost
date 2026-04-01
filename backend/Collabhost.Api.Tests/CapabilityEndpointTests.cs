using System.Net;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class CapabilityEndpointTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetAllCapabilities_ReturnsSeededCapabilities()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBe(11);
    }

    [Fact]
    public async Task GetAllCapabilities_IncludesExpectedFields()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var first = json.RootElement[0];
        first.TryGetProperty("slug", out _).ShouldBeTrue();
        first.TryGetProperty("displayName", out _).ShouldBeTrue();
        first.TryGetProperty("category", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetAllCapabilities_ContainsProcessCapability()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/capabilities");

        // Assert
        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var hasProcess = false;
        foreach (var element in json.RootElement.EnumerateArray())
        {
            if (element.GetProperty("slug").GetString() == StringCatalog.Capabilities.Process)
            {
                hasProcess = true;
                element.GetProperty("category").GetString().ShouldBe("behavioral");
                element.GetProperty("displayName").GetString().ShouldBe("Process Management");
                break;
            }
        }

        hasProcess.ShouldBeTrue();
    }
}
