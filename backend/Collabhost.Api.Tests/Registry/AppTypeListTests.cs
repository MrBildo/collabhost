using System.Net;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

[Collection("Api")]
public class AppTypeListTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Fact]
    public async Task ListAppTypes_ExcludesInternalTypes()
    {
        // system-service is marked isInternal in its JSON definition. The picker
        // must not surface it -- ProxyAppSeeder uses it via TypeStore.GetBySlug.
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get, "/api/v1/app-types"
        );
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(body).RootElement;

        items.ValueKind.ShouldBe(JsonValueKind.Array);

        var slugs = items.EnumerateArray()
            .Select(item => item.GetProperty("slug").GetString())
                .ToList();

        slugs.ShouldNotContain("system-service");

        // Operator-visible built-ins
        slugs.ShouldContain("dotnet-app");
        slugs.ShouldContain("nodejs-app");
        slugs.ShouldContain("static-site");
        slugs.ShouldContain("executable");
        slugs.ShouldContain("internal-service");
    }
}
