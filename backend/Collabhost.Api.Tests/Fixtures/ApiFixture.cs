using System.Text.Json.Nodes;

using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Collabhost.Api.Tests.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string? _dbDirectory;

    public const string AdminKey = "01INTEG0TEST0KEY00000000";

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateClient() => _factory.CreateClient();

    public Task InitializeAsync()
    {
        _dbDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_dbDirectory);

        var dbPath = Path.Combine(_dbDirectory, "collabhost.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder
            (
                builder =>
                {
                    builder.UseSetting("ConnectionStrings:Host", $"Data Source={dbPath}");
                    builder.UseSetting("Auth:AdminKey", AdminKey);
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

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }
