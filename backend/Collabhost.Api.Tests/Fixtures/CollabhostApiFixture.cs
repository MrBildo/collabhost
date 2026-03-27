using Collabhost.Api.Data;
using Collabhost.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Collabhost.Api.Tests.Fixtures;

public class CollabhostApiFixture : WebApplicationFactory<Program>
{
    public const string TestAdminKey = "01JTEST000000000000000ADMIN";

    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration
        (
            (_, config) =>
            {
                config.AddInMemoryCollection
                (
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["Auth:AdminKey"] = TestAdminKey
                    }
                );
            }
        );

        builder.ConfigureServices
        (
            services =>
            {
                var descriptor = services.SingleOrDefault
                (
                    d => d.ServiceType == typeof(DbContextOptions<CollabhostDbContext>)
                );

                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                services.AddDbContext<CollabhostDbContext>
                (
                    options => options.UseSqlite(_connection)
                );

                // Replace process runner with fake for tests
                var runnerDescriptor = services.SingleOrDefault
                (
                    d => d.ServiceType == typeof(IManagedProcessRunner)
                );

                if (runnerDescriptor is not null)
                {
                    services.Remove(runnerDescriptor);
                }

                services.AddSingleton<IManagedProcessRunner, FakeProcessRunner>();

                // Replace proxy config client with fake for tests
                var proxyDescriptor = services.SingleOrDefault
                (
                    d => d.ServiceType == typeof(IProxyConfigClient)
                );

                if (proxyDescriptor is not null)
                {
                    services.Remove(proxyDescriptor);
                }

                services.AddSingleton<IProxyConfigClient, FakeProxyConfigClient>();

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
                db.Database.EnsureCreated();
            }
        );
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Key", TestAdminKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connection?.Dispose();
        }

        base.Dispose(disposing);
    }
}
