using System.Net;
using System.Net.Http.Headers;

using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Portal;

// Each test owns its own WebApplicationFactory + temp wwwroot. The shared ApiFixture
// can't host these because PortalSpaFallbackMiddleware queries
// IWebHostEnvironment.WebRootFileProvider (fixed at host construction time) and the
// missing-shell degraded-mode test mutates the seeded wwwroot.
public sealed class PortalMiddlewareTests : IAsyncLifetime
{
    private const string _adminKey = "01PORTAL0TEST0KEY00000000";

    private string _dbDirectory = null!;
    private string _userTypesDirectory = null!;
    private string _wwwrootDirectory = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private static readonly string _seededIndexHtml =
        "<!doctype html><html><body data-test=\"portal-seeded-shell\"></body></html>";

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        _dbDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-portal-tests-db", Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_dbDirectory);

        _userTypesDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-portal-tests-usertypes", Guid.NewGuid().ToString("N")
        );

        _wwwrootDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-portal-tests-wwwroot", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(Path.Combine(_wwwrootDirectory, "assets"));
        await File.WriteAllTextAsync(Path.Combine(_wwwrootDirectory, "index.html"), _seededIndexHtml);
        await File.WriteAllTextAsync(Path.Combine(_wwwrootDirectory, "assets", "seeded.js"), "// seeded\n");

        var dbPath = Path.Combine(_dbDirectory, "collabhost.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder
            (
                builder =>
                {
                    builder.UseSetting("ConnectionStrings:Host", $"Data Source={dbPath}");
                    builder.UseSetting("Auth:AdminKey", _adminKey);
                    builder.UseSetting("TypeStore:UserTypesDirectory", _userTypesDirectory);
                    builder.UseSetting("Proxy:BaseDomain", "test.internal");
                    builder.UseSetting("Proxy:AdminApiUrl", "http://localhost:29999");
                    builder.UseSetting("Proxy:BinaryPath", "caddy");
                    builder.UseSetting("Proxy:ListenAddress", ":443");
                    builder.UseSetting("Proxy:CertLifetime", "168h");
                    builder.UseSetting("Hosting:ListenPort", "58400");
                    builder.UseSetting("Logging:LogLevel:Default", "Warning");
                    builder.UseSetting(WebHostDefaults.WebRootKey, _wwwrootDirectory);

                    builder.ConfigureServices
                    (
                        services =>
                        {
                            services.AddSingleton<ICaddyClient, NoopCaddyClient>();
                            services.AddSingleton<IBootVersionWriter, NoopBootVersionWriter>();
                        }
                    );
                }
            );

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();

        TryDelete(_dbDirectory);
        TryDelete(_userTypesDirectory);
        TryDelete(_wwwrootDirectory);
    }

    // ----- A: GET / unauthenticated -> seeded index.html -----

    [Fact]
    public async Task Get_Root_Unauthenticated_ReturnsIndexHtml()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe(_seededIndexHtml);
    }

    // ----- B: SPA deep-link (parameterized) -> seeded shell -----

    [Theory]
    [InlineData("/apps")]
    [InlineData("/apps/foo")]
    [InlineData("/users")]
    [InlineData("/system")]
    [InlineData("/routes")]
    public async Task Get_SpaDeepLink_Unauthenticated_ReturnsIndexHtml(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe(_seededIndexHtml);
    }

    // ----- C: static asset -> served by UseStaticFiles -----

    [Fact]
    public async Task Get_StaticAsset_Unauthenticated_ReturnsAsset()
    {
        var response = await _client.GetAsync(new Uri("/assets/seeded.js", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("// seeded\n");
    }

    // ----- D: API path with no key -> 401 JSON, NOT shell -----

    [Fact]
    public async Task Get_ApiPath_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync(new Uri("/api/v1/apps", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("Unauthorized");
    }

    // ----- E: /health and /openapi continue to skip -----

    [Fact]
    public async Task Get_Health_Unauthenticated_StillSkipsAuth()
    {
        var response = await _client.GetAsync(new Uri("/health", UriKind.Relative));

        // /health responds with 200 OK from the framework Health Check middleware.
        // The point of this test is that it does NOT return the SPA shell.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("portal-seeded-shell");
    }

    // ----- F: API + Accept: text/html -> still 401, NOT shell -----

    [Fact]
    public async Task Get_ApiPath_AcceptHtml_StillReachesHandler_AndAuths()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("portal-seeded-shell");
    }

    // ----- G: missing index.html -> falls through to auth -----

    [Fact]
    public async Task Get_Root_MissingIndexHtml_FallsThroughToAuth()
    {
        // Remove the seeded shell so the SPA-fallback predicate's existence check fails.
        // UseDefaultFiles will not rewrite /, UseStaticFiles will not find a disk hit, and
        // the fallback middleware will pass through. Auth then runs and returns 401.
        File.Delete(Path.Combine(_wwwrootDirectory, "index.html"));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ----- Dana absorption #2a: Accept: */* (curl default) -----

    [Fact]
    public async Task Get_SpaDeepLink_AcceptStarStar_ReturnsIndexHtml()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/apps");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe(_seededIndexHtml);
    }

    // ----- Dana absorption #2b: HEAD / -> 200 + correct headers, no body -----

    [Fact]
    public async Task Head_Root_Unauthenticated_ReturnsHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, "/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBeEmpty();
    }

    // ----- Dana absorption #2c: SPA deep-link with query string -----

    [Fact]
    public async Task Get_SpaDeepLinkWithQueryString_ReturnsIndexHtml()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/apps/foo?tab=settings");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe(_seededIndexHtml);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}

file sealed class NoopCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(false);

    public Task<bool> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

file sealed class NoopBootVersionWriter : IBootVersionWriter
{
    public void Write(string dataDirectory, string version)
    {
        // No-op
    }
}
