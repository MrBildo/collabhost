using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// Registration refuses system-service, but an install upgraded from a version that accepted it may
// already hold a system-service app an operator registered by hand. Only new registrations are
// refused: such an app is still listed, read, edited and deleted as before. The app here is written
// straight into the store, the way the earlier version's registration left it, because registration
// can no longer create one.
[Collection("Api")]
public class ExistingSystemServiceAppTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task ExistingSystemServiceApp_IsListedReadEditedAndDeleted()
    {
        var slug = $"test-existing-sys-{Guid.NewGuid().ToString("N")[..8]}";

        var appStore = _fixture.Services.GetRequiredService<AppStore>();

        await appStore.CreateAsync
        (
            new App
            {
                Slug = slug,
                DisplayName = "Hand-registered system service",
                AppTypeSlug = "system-service"
            },
            CancellationToken.None
        );

        try
        {
            using var listResponse = await SendAsync(HttpMethod.Get, "/api/v1/apps");

            listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var listBody = await listResponse.Content.ReadAsStringAsync();

            listBody.ShouldContain($"\"{slug}\"", Case.Sensitive);

            using var detailResponse = await SendAsync(HttpMethod.Get, $"/api/v1/apps/{slug}");

            detailResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var changes = new
            {
                changes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["environment-defaults"] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["variables"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["EXAMPLE_TOKEN"] = "value" }
                    }
                }
            };

            using var saveResponse = await SendAsync(HttpMethod.Put, $"/api/v1/apps/{slug}/settings", changes);

            saveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            var saveBody = await saveResponse.Content.ReadAsStringAsync();

            saveBody.ShouldContain("EXAMPLE_TOKEN", Case.Sensitive);

            using var deleteResponse = await SendAsync(HttpMethod.Delete, $"/api/v1/apps/{slug}");

            deleteResponse.IsSuccessStatusCode.ShouldBeTrue();

            using var afterDeleteResponse = await SendAsync(HttpMethod.Get, $"/api/v1/apps/{slug}");

            afterDeleteResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            using var cleanup = await SendAsync(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        }

        return await _client.SendAsync(request);
    }
}
