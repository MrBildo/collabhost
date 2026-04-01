using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Collabhost.Api.Tests.Fixtures;

public static class AppTestHelpers
{
    public static string ToTitleCase(string input)
    {
        var words = input.Split('-', ' ');
        return string.Join(" ", words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
    }

    public static object CreateValidRequest(string name, bool staticSite = false) =>
        new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = staticSite
                ? TestCatalogConstants.AppTypes.StaticSiteExternalId
                : TestCatalogConstants.AppTypes.ExecutableExternalId
        };

    public static async Task<string> CreateAppAsync(HttpClient client, string name, bool staticSite = false)
    {
        var request = CreateValidRequest(name, staticSite);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }
}
