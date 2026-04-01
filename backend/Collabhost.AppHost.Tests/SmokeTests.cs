using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Shouldly;

using Xunit;

namespace Collabhost.AppHost.Tests;

[Collection("AppHost")]
public class SmokeTests(AppHostFixture fixture)
{
    private readonly HttpClient _client = fixture.ApiClient;

    private static readonly string _executableAppTypeExternalId = "01KN0P7JYNJRAHGC01N17NFTWW";

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Status_ReturnsOk()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/status");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("status").GetString().ShouldBe("healthy");
    }

    [Fact]
    public async Task AppCrud_CreateAndGet_FieldsMatch()
    {
        // Arrange
        var slug = UniqueSlug("crud");
        var request = CreateAppRequest(slug);

        // Act — create
        var createResponse = await _client.PostAsJsonAsync("/api/v1/apps", request);

        // Assert — create
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createContent = await createResponse.Content.ReadAsStringAsync();
        var createJson = JsonDocument.Parse(createContent);
        var externalId = createJson.RootElement.GetProperty("externalId").GetString()!;
        externalId.ShouldNotBeNullOrWhiteSpace();

        // Act — get
        var getResponse = await _client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert — get
        getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var getContent = await getResponse.Content.ReadAsStringAsync();
        var getJson = JsonDocument.Parse(getContent);
        getJson.RootElement.GetProperty("name").GetString().ShouldBe(slug);
        getJson.RootElement.GetProperty("appType").GetProperty("displayName").GetString().ShouldBe("Executable");
    }

    [Fact]
    public async Task AppLifecycle_GetStatus_ReturnsStopped()
    {
        // Arrange — ProcessSupervisor uses a stub command that cannot execute in real environments.
        // Test verifies the status endpoint works for a never-started app.
        var slug = UniqueSlug("lifecycle");
        var externalId = await CreateAppAsync(slug);

        // Act — get status of never-started app
        var statusResponse = await _client.GetAsync($"/api/v1/apps/{externalId}/status");

        // Assert — stopped (never started)
        statusResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var statusContent = await statusResponse.Content.ReadAsStringAsync();
        var statusJson = JsonDocument.Parse(statusContent);
        statusJson.RootElement.GetProperty("processState").GetString().ShouldBe("Stopped");
    }

    [Fact]
    public async Task AuthRequired_NoHeader_Returns403()
    {
        // Arrange — create a fresh client without the auth header
        var unauthenticatedClient = new HttpClient
        {
            BaseAddress = _client.BaseAddress
        };

        // Act
        var response = await unauthenticatedClient.GetAsync("/api/v1/apps");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AppDelete_CreateThenDelete_Returns404OnGet()
    {
        // Arrange
        var slug = UniqueSlug("delete");
        var externalId = await CreateAppAsync(slug);

        // Act — delete
        var deleteResponse = await _client.DeleteAsync($"/api/v1/apps/{externalId}");

        // Assert — deleted
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Act — get after delete
        var getResponse = await _client.GetAsync($"/api/v1/apps/{externalId}");

        // Assert — gone
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Routes_CreatedAppAppearsInRouteList()
    {
        // Arrange
        var slug = UniqueSlug("routes");
        await CreateAppAsync(slug);

        // Act
        var response = await _client.GetAsync("/api/v1/routes");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        var routes = json.RootElement.GetProperty("routes");
        routes.GetArrayLength().ShouldBeGreaterThan(0);

        var matchingRoute = routes
            .EnumerateArray()
            .FirstOrDefault
            (
                r => r.GetProperty("domain").GetString()!
                    .StartsWith(slug, StringComparison.Ordinal)
            );
        matchingRoute.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Capabilities_ListReturnsSeededData()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/capabilities");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        json.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(10);
    }

    private static string UniqueSlug(string prefix) =>
        $"smoke-{prefix}-{Guid.NewGuid():N}"[..30];

    private static object CreateAppRequest(string slug)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "collabhost-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        return new
        {
            Name = slug,
            DisplayName = $"Smoke {slug}",
            AppTypeId = _executableAppTypeExternalId,
            CapabilityOverrides = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifact"] = new { location = tempPath }
            }
        };
    }

    private async Task<string> CreateAppAsync(string slug)
    {
        var request = CreateAppRequest(slug);
        var response = await _client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
