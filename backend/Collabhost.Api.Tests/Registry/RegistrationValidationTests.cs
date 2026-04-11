using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class RegistrationValidationTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // executable type slug (Phase 1b: CreateAppAsync now accepts slugs)
    private const string _executableAppTypeSlug = "executable";

    [Fact]
    public async Task RegisterWithInvalidSettings_ReturnsErrorAndDoesNotCreateApp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-invalid-reg-{suffix}";

        // Register with an invalid field name in the process section
        var createPayload = new
        {
            name = slug,
            displayName = "Invalid Registration Test",
            appTypeSlug = _executableAppTypeSlug,
            values = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["process"] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["executablePath"] = "/usr/bin/test"
                }
            }
        };

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

        var createResponse = await _client.SendAsync(createRequest);

        createResponse.StatusCode.ShouldBe
        (
            HttpStatusCode.BadRequest,
            "Registration with invalid settings should return 400"
        );

        // Verify the app was NOT created in the registry
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        getRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var getResponse = await _client.SendAsync(getRequest);

        getResponse.StatusCode.ShouldBe
        (
            HttpStatusCode.NotFound,
            "App should not exist after failed registration with invalid settings"
        );
    }

    [Fact]
    public async Task RegisterWithValidSettings_CreatesApp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-valid-reg-{suffix}";

        try
        {
            var createPayload = new
            {
                name = slug,
                displayName = "Valid Registration Test",
                appTypeSlug = _executableAppTypeSlug,
                values = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["process"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["command"] = "/usr/bin/test",
                        ["workingDirectory"] = "/tmp"
                    }
                }
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
            createRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            createRequest.Content = JsonContent.Create(createPayload, options: _jsonOptions);

            var createResponse = await _client.SendAsync(createRequest);

            createResponse.StatusCode.ShouldBe
            (
                HttpStatusCode.Created,
                "Registration with valid settings should succeed"
            );

            // Verify the app was created
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
            getRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var getResponse = await _client.SendAsync(getRequest);

            getResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            using var deleteRequest = new HttpRequestMessage
            (
                HttpMethod.Delete,
                $"/api/v1/apps/{slug}"
            );

            deleteRequest.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            await _client.SendAsync(deleteRequest);
        }
    }
}
