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

    private static readonly Guid _executableAppTypeId = new("acdb6994-2c22-42f5-bf89-68c42c9f980c");
    private static readonly Guid _neverRestartPolicyId = new("2f2f6115-b6ef-4db4-b3c7-200a4dbb3408");

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
        getJson.RootElement.GetProperty("appTypeName").GetString().ShouldBe("Executable");
        getJson.RootElement.GetProperty("port").GetInt32().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task AppLifecycle_StartAndStop_StatusTransitions()
    {
        // Arrange
        var slug = UniqueSlug("lifecycle");
        var externalId = await CreateAppAsync(slug, longRunning: true);

        // Act — start
        var startResponse = await _client.PostAsync($"/api/v1/apps/{externalId}/start", null);

        // Assert — running
        startResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var startContent = await startResponse.Content.ReadAsStringAsync();
        var startJson = JsonDocument.Parse(startContent);
        startJson.RootElement.GetProperty("processState").GetString().ShouldBe("Running");
        startJson.RootElement.GetProperty("pid").GetInt32().ShouldBeGreaterThan(0);

        // Act — stop
        var stopResponse = await _client.PostAsync($"/api/v1/apps/{externalId}/stop", null);

        // Assert — stopped
        stopResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var stopContent = await stopResponse.Content.ReadAsStringAsync();
        var stopJson = JsonDocument.Parse(stopContent);
        stopJson.RootElement.GetProperty("processState").GetString().ShouldBe("Stopped");
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

        var matchingRoute = routes.EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("domain").GetString()!.StartsWith(slug));
        matchingRoute.ValueKind.ShouldNotBe(JsonValueKind.Undefined);
        matchingRoute.GetProperty("proxyMode").GetString().ShouldBe("reverse_proxy");
    }

    [Fact]
    public async Task Update_SseStream_DeliversEvents()
    {
        // Arrange — create app with a real update command
        var slug = UniqueSlug("update");
        var request = new
        {
            Name = slug,
            DisplayName = $"Smoke {slug}",
            AppTypeId = _executableAppTypeId,
            InstallDirectory = "C:/temp",
            CommandLine = "cmd.exe",
            Arguments = "/c echo hello",
            WorkingDirectory = (string?)null,
            RestartPolicyId = _neverRestartPolicyId,
            HealthEndpoint = (string?)null,
            UpdateCommand = "echo SmokeUpdateDone",
            UpdateTimeoutSeconds = 30,
            AutoStart = false
        };

        var createResponse = await _client.PostAsJsonAsync("/api/v1/apps", request);
        createResponse.EnsureSuccessStatusCode();

        var createJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var externalId = createJson.RootElement.GetProperty("externalId").GetString()!;

        // Act — call update endpoint
        var updateResponse = await _client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert — SSE response with expected events
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        updateResponse.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        var body = await updateResponse.Content.ReadAsStringAsync();

        body.ShouldContain("event: status");
        body.ShouldContain("\"phase\":\"updating\"");
        body.ShouldContain("event: log");
        body.ShouldContain("SmokeUpdateDone");
        body.ShouldContain("event: result");
        body.ShouldContain("\"success\":true");
        body.ShouldContain("\"exitCode\":0");
    }

    private static string UniqueSlug(string prefix) =>
        $"smoke-{prefix}-{Guid.NewGuid():N}"[..30];

    private static object CreateAppRequest(string slug, bool longRunning = false) =>
        new
        {
            Name = slug,
            DisplayName = $"Smoke {slug}",
            AppTypeId = _executableAppTypeId,
            InstallDirectory = "C:/temp",
            CommandLine = longRunning ? "ping" : "cmd.exe",
            Arguments = longRunning ? "localhost -n 9999" : "/c echo hello",
            WorkingDirectory = (string?)null,
            RestartPolicyId = _neverRestartPolicyId,
            HealthEndpoint = (string?)null,
            UpdateCommand = (string?)null,
            AutoStart = false
        };

    private async Task<string> CreateAppAsync(string slug, bool longRunning = false)
    {
        var request = CreateAppRequest(slug, longRunning);
        var response = await _client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
