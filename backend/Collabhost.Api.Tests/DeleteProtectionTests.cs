using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class DeleteProtectionTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task Delete_AllowsRegularAppType()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();
        var externalId = await CreateAppAsync(client, "delete-regular");

        // Act
        var response = await client.DeleteAsync($"/api/v1/apps/{externalId}");

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Verify the app is actually gone
        var getResponse = await client.GetAsync($"/api/v1/apps/{externalId}");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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
            AppTypeId = IdentifierCatalog.AppTypes.ExecutableExternalId
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
