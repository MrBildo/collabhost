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

    public static object CreateValidRequest(string name, bool staticSite = false, string? artifactLocation = null)
    {
        var location = artifactLocation ?? CreateTempDirectory();

        return new
        {
            Name = name,
            DisplayName = $"{ToTitleCase(name)} App",
            AppTypeId = staticSite
                ? TestCatalogConstants.AppTypes.StaticSiteExternalId
                : TestCatalogConstants.AppTypes.ExecutableExternalId,
            CapabilityOverrides = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["artifact"] = new { location }
            }
        };
    }

    public static async Task<string> CreateAppAsync
    (
        HttpClient client,
        string name,
        bool staticSite = false,
        string? artifactLocation = null
    )
    {
        var request = CreateValidRequest(name, staticSite, artifactLocation);
        var response = await client.PostAsJsonAsync("/api/v1/apps", request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);
        return json.RootElement.GetProperty("externalId").GetString()!;
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "collabhost-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
