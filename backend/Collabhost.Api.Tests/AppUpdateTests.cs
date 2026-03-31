using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class AppUpdateTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task RunUpdate_AppNotFound_Returns404()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsync("/api/v1/apps/nonexistent-app-id/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RunUpdate_ReturnsNotSupported()
    {
        // Arrange — update endpoint is a stub in the capability model
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "update-stub-test");

        // Act
        var response = await client.PostAsync($"/api/v1/apps/{externalId}/update", null);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("NOT_SUPPORTED");
    }

    private static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    private static object CreateValidRequest(string name) =>
        new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = IdentifierCatalog.AppTypes.Executable
        };

    private static async Task<string> CreateAppAsync(HttpClient client, string name)
    {
        var request = CreateValidRequest(name);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
