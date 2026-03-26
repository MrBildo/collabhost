using System.Net;
using System.Text.Json;
using Collabhost.Api.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Collabhost.Api.Tests;

public class AuthTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task PublicEndpoint_WithoutKey_ReturnsOk()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ProtectedPath_WithoutKey_Returns403()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/apps");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("error").GetString().ShouldBe("Forbidden");
    }

    [Fact]
    public async Task ProtectedPath_WithWrongKey_Returns403()
    {
        // Arrange
        var client = _fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Key", "WRONG-KEY-VALUE");

        // Act
        var response = await client.GetAsync("/api/v1/apps");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProtectedPath_WithValidKey_DoesNotReturn403()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/apps");

        // Assert
        // The endpoint doesn't exist yet (returns 404), but importantly it should NOT be 403
        response.StatusCode.ShouldNotBe(HttpStatusCode.Forbidden);
    }
}
