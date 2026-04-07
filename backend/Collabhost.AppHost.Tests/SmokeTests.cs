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

    [Fact]
    public async Task HealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthyBody()
    {
        var response = await _client.GetAsync("/health");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldBe("Healthy");
    }

    [Fact]
    public async Task AlivenessCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/alive");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StatusEndpoint_ReturnsOk_WithoutAuth()
    {
        var response = await _client.GetAsync("/api/v1/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("status").GetString().ShouldBe("ok");
        doc.RootElement.GetProperty("hostname").GetString().ShouldNotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("version").GetString().ShouldNotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("uptimeSeconds").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("timestamp").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AppsEndpoint_RejectsMissingAuth()
    {
        var response = await _client.GetAsync("/api/v1/apps");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AppsEndpoint_WithAuth_ReturnsOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldStartWith("[");
    }

    [Fact]
    public async Task AppTypesEndpoint_WithAuth_ReturnsSeededTypes()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/app-types");
        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(body).RootElement;

        // Should have at least the 3 original seeded types (dotnet-app, nodejs-app, static-site)
        // plus executable and system-service from the migration
        items.GetArrayLength().ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task RoutesEndpoint_WithAuth_ReturnsOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/routes");
        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("baseDomain").GetString().ShouldNotBeNullOrWhiteSpace();
        doc.RootElement.GetProperty("routes").GetArrayLength().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task DashboardStatsEndpoint_WithAuth_ReturnsOk()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/dashboard/stats");
        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("totalApps").GetInt32().ShouldBeGreaterThanOrEqualTo(0);
        doc.RootElement.GetProperty("appTypes").GetInt32().ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task AppDetail_NotFound_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/nonexistent-app");
        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegistrationSchema_ForDotnetApp_ReturnsSchema()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types/dotnet-app/registration"
        );

        request.Headers.Add("X-User-Key", fixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("appType").GetProperty("name").GetString().ShouldBe("dotnet-app");
        doc.RootElement.GetProperty("sections").GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task StartTwoApps_Concurrently_BothComplete()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slugA = $"smoke-parallel-a-{suffix}";
        var slugB = $"smoke-parallel-b-{suffix}";

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        try
        {
            // Register two test apps
            var createA = new { name = slugA, displayName = "Parallel A", appTypeId = "01KN8K1MRQ0K06ADYJJ8VAXG5Y" };
            var createB = new { name = slugB, displayName = "Parallel B", appTypeId = "01KN8K1MRQ0K06ADYJJ8VAXG5Y" };

            using var createRequestA = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequestA.Headers.Add("X-User-Key", fixture.AdminKey);
            createRequestA.Content = JsonContent.Create(createA, options: jsonOptions);

            using var createRequestB = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequestB.Headers.Add("X-User-Key", fixture.AdminKey);
            createRequestB.Content = JsonContent.Create(createB, options: jsonOptions);

            var createResponseA = await _client.SendAsync(createRequestA);
            var createResponseB = await _client.SendAsync(createRequestB);

            createResponseA.StatusCode.ShouldBe(HttpStatusCode.Created);
            createResponseB.StatusCode.ShouldBe(HttpStatusCode.Created);

            // Fire both start requests concurrently
            using var startRequestA = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slugA}/start");
            startRequestA.Headers.Add("X-User-Key", fixture.AdminKey);

            using var startRequestB = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/apps/{slugB}/start");
            startRequestB.Headers.Add("X-User-Key", fixture.AdminKey);

            var startTaskA = _client.SendAsync(startRequestA);
            var startTaskB = _client.SendAsync(startRequestB);

            // Both should complete within 5 seconds -- deadlock would cause timeout
            var bothCompleted = Task.WhenAll(startTaskA, startTaskB);
            var completedTask = await Task.WhenAny(bothCompleted, Task.Delay(TimeSpan.FromSeconds(5)));

            completedTask.ShouldBe(bothCompleted, "Concurrent start requests deadlocked behind a global lock");

            // Both requests completed (may be errors since no actual executable, but they didn't deadlock)
            var responseA = await startTaskA;
            var responseB = await startTaskB;

            responseA.ShouldNotBeNull();
            responseB.ShouldNotBeNull();
        }
        finally
        {
            // Clean up: delete both test apps
            using var deleteA = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slugA}");
            deleteA.Headers.Add("X-User-Key", fixture.AdminKey);

            using var deleteB = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slugB}");
            deleteB.Headers.Add("X-User-Key", fixture.AdminKey);

            await _client.SendAsync(deleteA);
            await _client.SendAsync(deleteB);
        }
    }
}
