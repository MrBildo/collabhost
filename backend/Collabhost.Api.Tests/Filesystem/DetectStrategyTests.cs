using System.Net;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Filesystem;

[Collection("Api")]
public class DetectStrategyTests : IDisposable
{
    private readonly HttpClient _client;

    private readonly string _tempDirectory = Path.Combine
    (
        Path.GetTempPath(),
        "collabhost-detect-tests",
        Guid.NewGuid().ToString("N")
    );

    public DetectStrategyTests(ApiFixture fixture)
    {
        _client = fixture.Client;
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task DetectStrategy_DotNetWithRuntimeConfig_ReturnsDotNetRuntimeConfiguration()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "MyApp.runtimeconfig.json"),
            "{}"
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("dotNetRuntimeConfiguration");

        var evidence = root.GetProperty("evidence");

        evidence.GetArrayLength().ShouldBeGreaterThan(0);
        evidence[0].GetString().ShouldBe("MyApp.runtimeconfig.json");
    }

    [Fact]
    public async Task DetectStrategy_DotNetWithCsproj_ReturnsDotNetProject()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "MyApp.csproj"),
            "<Project />"
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("dotNetProject");

        var evidence = root.GetProperty("evidence");

        evidence.GetArrayLength().ShouldBe(1);
        evidence[0].GetString().ShouldBe("MyApp.csproj");
    }

    [Fact]
    public async Task DetectStrategy_DotNetPrefersRuntimeConfigOverCsproj()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "MyApp.runtimeconfig.json"),
            "{}"
        );

        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "MyApp.csproj"),
            "<Project />"
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("dotNetRuntimeConfiguration");
    }

    [Fact]
    public async Task DetectStrategy_NodeJsWithPackageJsonAndStartScript_ReturnsPackageJson()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "package.json"),
            """{"scripts":{"start":"node index.js"}}"""
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=nodejs-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("packageJson");

        root.GetProperty("evidence")[0].GetString()
            .ShouldBe("package.json");
    }

    [Fact]
    public async Task DetectStrategy_NodeJsWithoutStartScript_ReturnsManual()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "package.json"),
            """{"scripts":{"build":"tsc"}}"""
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=nodejs-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");
    }

    [Fact]
    public async Task DetectStrategy_EmptyDirectory_ReturnsManual()
    {
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        root.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");

        root.GetProperty("evidence").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task DetectStrategy_StaticSiteEmptyDirectory_ReturnsNotApplicable()
    {
        // static-site doesn't go through process discovery, so the strategy field
        // carries the free-string literal "notApplicable" -- card #220 Bill ruling 1.
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=static-site"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("notApplicable");
    }

    [Fact]
    public async Task DetectStrategy_UnknownAppType_ReturnsManual()
    {
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=fake-type"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        doc.RootElement.GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");
    }

    [Fact]
    public async Task DetectStrategy_MissingPath_Returns400()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            "/api/v1/filesystem/detect-strategy?appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // Card #344: omitting appTypeSlug returns a per-type map covering every
    // AppType the collector has detection rules for. Previously this returned
    // 400; the new shape decouples callers from form-step ordering (caller no
    // longer has to choose an AppType before asking "what's in this directory?").
    [Fact]
    public async Task DetectStrategy_MissingAppTypeSlug_ReturnsPerTypeMap()
    {
        await File.WriteAllTextAsync
        (
            Path.Combine(_tempDirectory, "MyApp.csproj"),
            "<Project />"
        );

        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var perType = root.GetProperty("perType");

        // Every AppType the collector handles must appear.
        perType.GetProperty("dotnet-app").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("dotNetProject");
        perType.GetProperty("dotnet-app").GetProperty("evidence")[0].GetString()
            .ShouldBe("MyApp.csproj");

        perType.GetProperty("nodejs-app").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");
        perType.GetProperty("nodejs-app").GetProperty("evidence").GetArrayLength()
            .ShouldBe(0);

        perType.GetProperty("static-site").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("notApplicable");

        perType.GetProperty("executable").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("notApplicable");
    }

    [Fact]
    public async Task DetectStrategy_MissingAppTypeSlug_EmptyDirectory_ReturnsPerTypeMapWithAllManual()
    {
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        var perType = doc.RootElement.GetProperty("perType");

        // dotnet-app / nodejs-app return "manual" on an empty directory.
        perType.GetProperty("dotnet-app").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");
        perType.GetProperty("nodejs-app").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("manual");

        // static-site / executable return "notApplicable" on an empty directory
        // (their pre-#344 single-type behavior is preserved per slug).
        perType.GetProperty("static-site").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("notApplicable");
        perType.GetProperty("executable").GetProperty("suggestedStrategy").GetString()
            .ShouldBe("notApplicable");
    }

    [Fact]
    public async Task DetectStrategy_MissingAppTypeSlug_NoAuth_Returns401()
    {
        // The per-type shape is still authenticated -- omitting appTypeSlug only
        // changes the response shape, not the auth contract.
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}"
        );

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DetectStrategy_MissingAppTypeSlug_NonexistentPath_Returns404()
    {
        // Path validation runs before the slug branch -- a nonexistent path
        // still 404s regardless of whether appTypeSlug is provided.
        var fakePath = Uri.EscapeDataString
        (
            @"C:\this-path-does-not-exist-" + Guid.NewGuid().ToString("N")
        );

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={fakePath}"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DetectStrategy_NoAuth_Returns401()
    {
        var encoded = Uri.EscapeDataString(_tempDirectory);

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={encoded}&appTypeSlug=dotnet-app"
        );

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DetectStrategy_NonexistentPath_Returns404()
    {
        var fakePath = Uri.EscapeDataString
        (
            @"C:\this-path-does-not-exist-" + Guid.NewGuid().ToString("N")
        );

        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/filesystem/detect-strategy?path={fakePath}&appTypeSlug=dotnet-app"
        );

        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
