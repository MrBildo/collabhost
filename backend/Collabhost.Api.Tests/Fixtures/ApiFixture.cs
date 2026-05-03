using System.Text.Json.Nodes;

using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Collabhost.Api.Tests.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string? _dbDirectory;
    private string? _userTypesDirectory;
    private string? _wwwrootDirectory;

    public const string AdminKey = "01INTEG0TEST0KEY00000000";

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateClient() => _factory.CreateClient();

    public string WwwrootDirectory =>
        _wwwrootDirectory
            ?? throw new InvalidOperationException("ApiFixture not initialized.");

    public async Task InitializeAsync()
    {
        // Null the env var so a developer shell with COLLABHOST_USER_TYPES_PATH set does not
        // shadow the UseSetting path below. Env var takes precedence in TypeStoreRegistration
        // (env > config > default) so without this the test would silently use the developer's
        // path instead of the per-test temp dir.
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        _dbDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_dbDirectory);

        // Isolated per-test user-types directory. Preflight creates it if missing; we want a
        // clean slate so test runs don't leave a stray 'UserTypes' dir next to the test binaries.
        _userTypesDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests-usertypes", Guid.NewGuid().ToString("N")
        );

        // Per-test temp wwwroot. Seeded with index.html and assets/seeded.js so
        // PortalReachabilityCheck reports Ok and the SPA-fallback middleware finds
        // a real shell to serve. Tests that exercise the missing-shell degraded mode
        // delete the seeded files at the start of the test (see Portal middleware tests).
        _wwwrootDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests-wwwroot", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(Path.Combine(_wwwrootDirectory, "assets"));
        await File.WriteAllTextAsync
        (
            Path.Combine(_wwwrootDirectory, "index.html"),
            "<!doctype html><html><body data-test=\"seeded-shell\"></body></html>"
        );
        await File.WriteAllTextAsync
        (
            Path.Combine(_wwwrootDirectory, "assets", "seeded.js"),
            "// seeded\n"
        );

        var dbPath = Path.Combine(_dbDirectory, "collabhost.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder
            (
                builder =>
                {
                    builder.UseSetting("ConnectionStrings:Host", $"Data Source={dbPath}");
                    builder.UseSetting("Auth:AdminKey", AdminKey);
                    builder.UseSetting("TypeStore:UserTypesDirectory", _userTypesDirectory);
                    builder.UseSetting("Proxy:BaseDomain", "test.internal");
                    builder.UseSetting("Proxy:AdminApiUrl", "http://localhost:29999");
                    builder.UseSetting("Proxy:BinaryPath", "caddy");
                    builder.UseSetting("Proxy:ListenAddress", ":443");
                    builder.UseSetting("Proxy:CertLifetime", "168h");
                    builder.UseSetting("Hosting:ListenPort", "58400");
                    builder.UseSetting(WebHostDefaults.WebRootKey, _wwwrootDirectory);

                    // Suppress verbose EF Core SQL and ASP.NET Core info logging during tests.
                    // When dotnet test runs multiple projects concurrently, high-volume console
                    // output deadlocks the .NET 10 terminal logger's pipe buffer on Linux.
                    builder.UseSetting("Logging:LogLevel:Default", "Warning");

                    builder.ConfigureServices
                    (
                        services =>
                        {
                            // Replace the Caddy client with a no-op fake
                            services.AddSingleton<ICaddyClient, FakeCaddyClient>();

                            // Suppress the .last-boot-version sentinel write under
                            // WebApplicationFactory<Program>. ApplicationStarted fires per test,
                            // so without this the temp data directory gets a stray sentinel
                            // (PR #95 MED-2).
                            services.AddSingleton<IBootVersionWriter, NoopBootVersionWriter>();
                        }
                    );
                }
            );

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        await _factory.DisposeAsync();

        if (_dbDirectory is not null && Directory.Exists(_dbDirectory))
        {
            try
            {
                Directory.Delete(_dbDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_userTypesDirectory is not null && Directory.Exists(_userTypesDirectory))
        {
            try
            {
                Directory.Delete(_userTypesDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_wwwrootDirectory is not null && Directory.Exists(_wwwrootDirectory))
        {
            try
            {
                Directory.Delete(_wwwrootDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}

// Sealed: file-scoped test fake, no inheritance needed
file sealed class FakeCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<JsonObject?>(null);
}

// Sealed: file-scoped test fake, no inheritance needed
file sealed class NoopBootVersionWriter : IBootVersionWriter
{
    public void Write(string dataDirectory, string version)
    {
        // No-op: integration tests should not leave a .last-boot-version sentinel behind.
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }
