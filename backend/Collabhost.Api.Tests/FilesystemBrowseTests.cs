using System.Net;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

using static Collabhost.Api.Tests.Fixtures.AppTestHelpers;

namespace Collabhost.Api.Tests;

public class FilesystemBrowseTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task Browse_WithoutPath_ReturnsFilesystemRoots()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/filesystem/browse");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("currentPath").ValueKind.ShouldBe(JsonValueKind.Null);
        json.RootElement.GetProperty("parent").ValueKind.ShouldBe(JsonValueKind.Null);

        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBeGreaterThan(0);

        // Each entry should have name and path
        var first = entries[0];
        first.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
        first.GetProperty("path").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Browse_ValidPath_ReturnsDirectories()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var tempDir = CreateTempDirectory();
        var subDir = Path.Combine(tempDir, "child-dir");
        Directory.CreateDirectory(subDir);

        // Act
        var response = await client.GetAsync($"/api/v1/filesystem/browse?path={Uri.EscapeDataString(tempDir)}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        json.RootElement.GetProperty("currentPath").GetString().ShouldBe(tempDir);
        json.RootElement.GetProperty("parent").GetString().ShouldNotBeNullOrWhiteSpace();

        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(1);
        entries[0].GetProperty("name").GetString().ShouldBe("child-dir");
    }

    [Fact]
    public async Task Browse_NonexistentPath_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/filesystem/browse?path=C%3A%5Cnonexistent-path-12345");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Browse_RelativePathSegments_Returns400()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/v1/filesystem/browse?path=C%3A%5CUsers%5C..%5CWindows");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Browse_EntriesSortedAlphabetically()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var tempDir = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(tempDir, "zebra"));
        Directory.CreateDirectory(Path.Combine(tempDir, "alpha"));
        Directory.CreateDirectory(Path.Combine(tempDir, "middle"));

        // Act
        var response = await client.GetAsync($"/api/v1/filesystem/browse?path={Uri.EscapeDataString(tempDir)}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        var entries = json.RootElement.GetProperty("entries");
        entries.GetArrayLength().ShouldBe(3);
        entries[0].GetProperty("name").GetString().ShouldBe("alpha");
        entries[1].GetProperty("name").GetString().ShouldBe("middle");
        entries[2].GetProperty("name").GetString().ShouldBe("zebra");
    }

    [Fact]
    public async Task Browse_WithoutAuth_Returns403()
    {
        // Arrange
        var client = _fixture.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/filesystem/browse");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
