using System.Net.Http.Headers;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Every response Collabhost itself serves -- the dashboard shell, its static assets, the SPA
// deep-link fallback, the public API and the auth wall's 401 -- carries the headers that stop
// another page from framing the dashboard and stop browsers from guessing content types. The
// dashboard keeps an admin key in localStorage, and every hosted app shares its site, so a hosted
// app's page could otherwise frame a logged-in dashboard and steer an operator's clicks.
[Collection("Api")]
public class ResponseSecurityHeadersTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    [Theory]
    [InlineData("/", "text/html")]
    [InlineData("/apps/some-app", "text/html")]
    [InlineData("/assets/seeded.js", "*/*")]
    [InlineData("/api/v1/status", "application/json")]
    [InlineData("/api/v1/apps", "application/json")]
    public async Task Response_CarriesFramingAndContentTypeHeaders(string path, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        using var response = await _client.SendAsync(request);

        HeaderValue(response, "X-Frame-Options").ShouldBe("DENY");
        HeaderValue(response, "Content-Security-Policy").ShouldBe("frame-ancestors 'none'");
        HeaderValue(response, "X-Content-Type-Options").ShouldBe("nosniff");
        HeaderValue(response, "Referrer-Policy").ShouldBe("no-referrer");
    }

    // HttpClient files a header under the response or the content collection by its RFC class, so
    // look in both rather than assume which one these land in.
    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(", ", values)
            : null;
}
