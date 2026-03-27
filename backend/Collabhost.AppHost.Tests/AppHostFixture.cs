using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

using Xunit;

namespace Collabhost.AppHost.Tests;

public class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? _app;

    public const string TestAdminKey = "smoke-test-admin-key";

    public HttpClient ApiClient { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Set auth key as env var — inherited by child API process
        Environment.SetEnvironmentVariable("AUTH__ADMINKEY", TestAdminKey);

        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Collabhost_AppHost>();

        _app = await appHost.BuildAsync();

        await _app.StartAsync();

        // Wait only for the API — frontend may fail without Node.js
        await _app.ResourceNotifications
            .WaitForResourceHealthyAsync("api")
            .WaitAsync(TimeSpan.FromSeconds(30));

        ApiClient = _app.CreateHttpClient("api");
        ApiClient.DefaultRequestHeaders.Add("X-User-Key", TestAdminKey);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        Environment.SetEnvironmentVariable("AUTH__ADMINKEY", null);
    }
}

[CollectionDefinition("AppHost")]
public class AppHostCollection : ICollectionFixture<AppHostFixture> { }
