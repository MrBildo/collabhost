using Collabhost.Api.Data;
using Collabhost.Api.Services.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class ProxyAppSeederTests(CollabhostApiFixture fixture) : IClassFixture<CollabhostApiFixture>
{
    private readonly CollabhostApiFixture _fixture = fixture;

    [Fact]
    public async Task SeedAsync_CreatesProxyApp_WhenNoneExists()
    {
        // Arrange
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var settings = CreateSettings(GetKnownExistingBinary());
        var logger = NullLogger<ProxyAppSeeder>.Instance;
        var seeder = new ProxyAppSeeder(db, settings, logger);

        await RemoveExistingProxyAppsAsync(db);

        // Act
        await seeder.SeedAsync(CancellationToken.None);

        // Assert
        var proxyApp = await db.Apps
            .SingleOrDefaultAsync(a => a.Name == Domain.Values.AppSlugValue.Create("proxy"));

        proxyApp.ShouldNotBeNull();
        proxyApp.Name.Value.ShouldBe("proxy");
        proxyApp.DisplayName.ShouldBe("Proxy");
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_DoesNotCreateDuplicate()
    {
        // Arrange
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var settings = CreateSettings(GetKnownExistingBinary());
        var logger = NullLogger<ProxyAppSeeder>.Instance;
        var seeder = new ProxyAppSeeder(db, settings, logger);

        await RemoveExistingProxyAppsAsync(db);
        await seeder.SeedAsync(CancellationToken.None);

        // Act — seed second time
        await seeder.SeedAsync(CancellationToken.None);

        // Assert
        var count = await db.Apps
            .CountAsync(a => a.Name == Domain.Values.AppSlugValue.Create("proxy"));

        count.ShouldBe(1);
    }

    [Fact]
    public async Task SeedAsync_SkipsWhenBinaryNotFound()
    {
        // Arrange
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var settings = CreateSettings("C:\\nonexistent\\path\\to\\caddy.exe");
        var logger = NullLogger<ProxyAppSeeder>.Instance;
        var seeder = new ProxyAppSeeder(db, settings, logger);

        await RemoveExistingProxyAppsAsync(db);

        // Act
        await seeder.SeedAsync(CancellationToken.None);

        // Assert — no proxy app should be created
        var count = await db.Apps
            .CountAsync(a => a.Name == Domain.Values.AppSlugValue.Create("proxy"));

        count.ShouldBe(0);
    }

    private static ProxySettings CreateSettings(string binaryPath) =>
        new()
        {
            BaseDomain = "collab.internal",
            AdminApiUrl = "http://localhost:2019",
            BinaryPath = binaryPath,
            ListenAddress = ":443",
            CertLifetime = "168h",
            SelfPort = 58400
        };

    private static async Task RemoveExistingProxyAppsAsync(CollabhostDbContext db)
    {
        var existing = await db.Apps
            .Where(a => a.Name == Domain.Values.AppSlugValue.Create("proxy"))
            .ToListAsync();

        db.Apps.RemoveRange(existing);
        await db.SaveChangesAsync();
    }

    private static string GetKnownExistingBinary() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
            : "/bin/sh";
}
