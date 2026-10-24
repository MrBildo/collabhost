using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// routing.domainPattern is set once, at registration, by whoever registers the app -- which an Agent
// key may do. The proxy routes by hostname, so the value decides which requests reach the app. These
// pin what registration enforces on it: the pattern is a hostname (no wildcard, no proxy placeholder,
// no empty or hyphen-edged label), and the hostname it resolves to is not one another app or the
// dashboard already answers on. The fixture's base domain is "test.internal", so the dashboard is
// "collabhost.test.internal" and an app registered with the default pattern is "<slug>.test.internal".
[Collection("Api")]
public class DomainPatternRegistrationTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task DomainPattern_ClaimingAnotherAppsHostname_Returns409AndDoesNotCreateApp()
    {
        var suffix = NewSuffix();
        var victim = $"victim-{suffix}";
        var attacker = $"attacker-{suffix}";

        (await RegisterStaticSiteAsync(victim)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await RegisterStaticSiteAsync(attacker, domainPattern: $"{victim}.test.internal");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain($"{victim}.test.internal");
        (await GetAppStatusAsync(attacker)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DomainPattern_DifferingOnlyInCase_IsTheSameHostname_Returns409()
    {
        var suffix = NewSuffix();
        var victim = $"victim-{suffix}";
        var attacker = $"attacker-{suffix}";

        (await RegisterStaticSiteAsync(victim)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await RegisterStaticSiteAsync
        (
            attacker,
            domainPattern: $"{victim}.test.internal".ToUpperInvariant()
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetAppStatusAsync(attacker)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DomainPattern_ClaimingTheDashboardHostname_Returns409()
    {
        var attacker = $"attacker-{NewSuffix()}";

        var response = await RegisterStaticSiteAsync(attacker, domainPattern: "collabhost.test.internal");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetAppStatusAsync(attacker)).ShouldBe(HttpStatusCode.NotFound);
    }

    // The reverse direction: an earlier app's custom hostname is taken too, so a later app whose
    // default pattern would resolve to it is refused rather than left to shadow or be shadowed.
    [Fact]
    public async Task DefaultPattern_ResolvingToAnEarlierCustomHostname_Returns409()
    {
        var suffix = NewSuffix();
        var shared = $"shared-{suffix}";

        var first = await RegisterStaticSiteAsync($"first-{suffix}", domainPattern: $"{shared}.test.internal");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await RegisterStaticSiteAsync(shared);

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await GetAppStatusAsync(shared)).ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("*.test.internal")]          // a wildcard would answer for every subdomain
    [InlineData("{http.request.host}")]      // a proxy placeholder would match any hostname
    [InlineData("{slug}..{baseDomain}")]     // empty label
    [InlineData(".{baseDomain}")]            // leading dot
    [InlineData("{slug}.{baseDomain}.")]     // trailing dot
    [InlineData("-{slug}.{baseDomain}")]     // label starting with a hyphen
    [InlineData("my app.{baseDomain}")]      // whitespace
    [InlineData("{slug}/admin.{baseDomain}")] // path separator
    [InlineData("{slug}.{baseDomain}:8443")] // port
    public async Task DomainPattern_NotAHostname_Returns400AndDoesNotCreateApp(string domainPattern)
    {
        var slug = $"shape-{NewSuffix()}";

        var response = await RegisterStaticSiteAsync(slug, domainPattern);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("domainPattern");
        (await GetAppStatusAsync(slug)).ShouldBe(HttpStatusCode.NotFound);
    }

    // Positive control for the two refusals above: a hostname nobody else uses, outside the base
    // domain and built from the slug, is still a legitimate choice.
    [Fact]
    public async Task DomainPattern_UnclaimedCustomHostname_Registers()
    {
        var suffix = NewSuffix();
        var slug = $"custom-{suffix}";

        var response = await RegisterStaticSiteAsync(slug, domainPattern: $"{{slug}}.apps-{suffix}.example.com");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    // serveMode is decided by the app type, so registration takes no value for it either.
    [Fact]
    public async Task ServeMode_AtRegistration_Returns400AndDoesNotCreateApp()
    {
        var slug = $"servemode-{NewSuffix()}";

        var response = await RegisterAsync
        (
            slug,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["serveMode"] = "ReverseProxy"
            }
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("serveMode");
        (await GetAppStatusAsync(slug)).ShouldBe(HttpStatusCode.NotFound);
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];

    private Task<HttpResponseMessage> RegisterStaticSiteAsync(string slug, string? domainPattern = null) =>
        RegisterAsync
        (
            slug,
            domainPattern is null
                ? null
                : new Dictionary<string, object>(StringComparer.Ordinal) { ["domainPattern"] = domainPattern }
        );

    private async Task<HttpResponseMessage> RegisterAsync(string slug, Dictionary<string, object>? routing)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);

        if (routing is not null)
        {
            values["routing"] = routing;
        }

        var payload = new
        {
            name = slug,
            displayName = slug,
            appTypeSlug = "static-site",
            values
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(payload, options: _jsonOptions);

        return await _client.SendAsync(request);
    }

    private async Task<HttpStatusCode> GetAppStatusAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request);

        return response.StatusCode;
    }
}
