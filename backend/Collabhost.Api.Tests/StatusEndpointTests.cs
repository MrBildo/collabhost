using System.Net;
using System.Text.Json;
using Collabhost.Api.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Collabhost.Api.Tests;

public class StatusEndpointTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task GetStatus_ReturnsOkWithExpectedShape()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var root = json.RootElement;

        root.GetProperty("status").GetString().ShouldBe("healthy");
        root.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        root.TryGetProperty("timestamp", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task GetStatus_WorksWithoutAuthHeader()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
