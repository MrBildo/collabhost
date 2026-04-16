using Aspire.Hosting;
using Aspire.Hosting.Testing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Xunit;

namespace Collabhost.AppHost.Tests;

public class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? _app;
    private string? _dbDirectory;

    // A known admin key injected via env var so tests can authenticate
    public string AdminKey { get; } = "01SMOKE0TEST0KEY000000000";

    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Create a temp directory for the SQLite database
        _dbDirectory = Path.Combine(Path.GetTempPath(), "collabhost-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dbDirectory);

        var dbPath = Path.Combine(_dbDirectory, "collabhost.db");

        // Override the connection string via env var -- Aspire passes this to the API process
        Environment.SetEnvironmentVariable("ConnectionStrings__Host", $"Data Source={dbPath}");

        // Set a known admin key for auth
        Environment.SetEnvironmentVariable("Auth__AdminKey", AdminKey);

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Collabhost_AppHost>();

        // Suppress verbose console logging to prevent test runner pipe buffer deadlocks on Linux.
        // Aspire forwards all managed resource stdout/stderr to ILogger under the category
        // "Collabhost.AppHost.Resources.{name}", which produces 700+ lines of EF Core SQL,
        // ASP.NET Core info, and HTTP client traces. When dotnet test runs multiple projects
        // concurrently, this volume deadlocks the terminal logger's output buffer on Linux.
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
        });

        // Accept untrusted dev certificates for AppHost-level health checks in CI.
        // Aspire's WithHttpHealthCheck polls the resource's HTTPS endpoint from inside
        // the AppHost process. On CI runners the Aspire dev cert is not trusted, so the
        // health check fails with UntrustedRoot every 5 seconds and the test hangs until
        // the WaitForResourceHealthyAsync timeout expires. Accepting any server certificate
        // here allows the health check to succeed over HTTPS without a trusted cert.
        appHost.Services.ConfigureHttpClientDefaults(http =>
        {
#pragma warning disable MA0039 // Intentional: test fixture must accept untrusted Aspire dev certs in CI
            http.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
#pragma warning restore MA0039
        });

        _app = await appHost.BuildAsync();

        await _app.StartAsync();

        // Wait only for the API -- frontend may fail without Node.js
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("api")
            .WaitAsync(TimeSpan.FromSeconds(30));

        ApiClient = _app.CreateHttpClient("api");
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("ConnectionStrings__Host", null);
        Environment.SetEnvironmentVariable("Auth__AdminKey", null);

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

[CollectionDefinition("AppHost")]
public class AppHostCollection : ICollectionFixture<AppHostFixture> { }
