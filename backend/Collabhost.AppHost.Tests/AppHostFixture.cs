using Aspire.Hosting;
using Aspire.Hosting.Testing;

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
