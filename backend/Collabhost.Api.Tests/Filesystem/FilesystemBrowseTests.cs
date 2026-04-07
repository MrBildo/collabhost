using System.Net;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Filesystem;

[Collection("Api")]
public class FilesystemBrowseTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task Browse_NoPath_ReturnsDriveRoots()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/filesystem/browse");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("parent").ValueKind.ShouldBe(JsonValueKind.Null);

        var directories = root.GetProperty("directories");

        directories.GetArrayLength().ShouldBeGreaterThan(0);

        // On Windows, at least C:\ should be present
        var hasRootDrive = false;

        foreach (var entry in directories.EnumerateArray())
        {
            entry.GetProperty("name").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("path").GetString().ShouldNotBeNullOrWhiteSpace();

            if (string.Equals(entry.GetProperty("name").GetString(), "C:", StringComparison.OrdinalIgnoreCase))
            {
                hasRootDrive = true;
            }
        }

        hasRootDrive.ShouldBeTrue("Drive roots should include C:");
    }

    [Fact]
    public async Task Browse_ValidDirectory_ReturnsChildren()
    {
        // Use the temp directory — guaranteed to exist and be accessible
        var tempPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var encoded = Uri.EscapeDataString(tempPath);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/browse?path={encoded}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("currentPath").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("parent").GetString().ShouldNotBeNullOrWhiteSpace();

        // Directories should be an array (may be empty but that is valid)
        root.GetProperty("directories").ValueKind.ShouldBe(JsonValueKind.Array);
    }

    [Fact]
    public async Task Browse_NonexistentPath_Returns404()
    {
        var fakePath = Uri.EscapeDataString(@"C:\this-path-does-not-exist-" + Guid.NewGuid().ToString("N"));

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/browse?path={fakePath}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Browse_InvalidPathCharacters_Returns400()
    {
        // Null character is always invalid in a path
        var invalidPath = Uri.EscapeDataString("C:\\\0invalid");

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/browse?path={invalidPath}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Browse_NoAuthKey_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/filesystem/browse");

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Browse_DirectoryWithChildren_ReturnsNameAndPath()
    {
        // Create a temp directory with a known child
        var parentDirectory = Path.Combine(Path.GetTempPath(), "collabhost-browse-test-" + Guid.NewGuid().ToString("N"));
        var childDirectory = Path.Combine(parentDirectory, "test-child");

        Directory.CreateDirectory(childDirectory);

        try
        {
            var encoded = Uri.EscapeDataString(parentDirectory);

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/filesystem/browse?path={encoded}"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var directories = root.GetProperty("directories");

            directories.GetArrayLength().ShouldBe(1);

            var child = directories[0];

            child.GetProperty("name").GetString().ShouldBe("test-child");
            child.GetProperty("path").GetString().ShouldBe(childDirectory);
        }
        finally
        {
            Directory.Delete(parentDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Browse_ParentOfDriveRoot_ReturnsNullParent()
    {
        var encoded = Uri.EscapeDataString(@"C:\");

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/browse?path={encoded}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("currentPath").GetString().ShouldBe(@"C:\");
        root.GetProperty("parent").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
