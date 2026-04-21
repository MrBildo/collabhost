using System.Text.Json.Nodes;

using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Collabhost.Api.Tests.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string? _dbDirectory;
    private string? _userTypesDirectory;

    public const string AdminKey = "01INTEG0TEST0KEY00000000";

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateClient() => _factory.CreateClient();

    public Task InitializeAsync()
    {
        // Null the env var so a developer shell with COLLABHOST_USER_TYPES_PATH set does not
        // shadow the UseSetting path below. Env var takes precedence in TypeStoreRegistration
        // (§12.3 precedence: env > config > default) so without this the test would silently
        // use the developer's path instead of the per-test temp dir.
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
                    builder.UseSetting("Proxy:SelfPort", "58400");

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

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        await _factory.DisposeAsync();

        if (_dbDirectory is not null && Directory.Exists(_dbDirectory))
        {
            try
            {
                Directory.Delete(_dbDirectory, recursive: true);
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
                Directory.Delete(_userTypesDirectory, recursive: true);
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

    public Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(true);

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
