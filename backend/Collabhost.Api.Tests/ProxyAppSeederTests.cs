using Collabhost.Api.Data;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;
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
            .SingleOrDefaultAsync(a => a.AppTypeId == IdentifierCatalog.AppTypes.ProxyService);

        proxyApp.ShouldNotBeNull();
        proxyApp.Name.Value.ShouldBe("proxy");
        proxyApp.DisplayName.ShouldBe("Proxy");
        proxyApp.AppTypeId.ShouldBe(IdentifierCatalog.AppTypes.ProxyService);
        proxyApp.Arguments.ShouldBe("run --resume");
        proxyApp.RestartPolicyId.ShouldBe(IdentifierCatalog.RestartPolicies.Always);
        proxyApp.AutoStart.ShouldBeTrue();
        proxyApp.Port.ShouldBeNull();
        proxyApp.HealthEndpoint.ShouldBeNull();
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
            .CountAsync(a => a.AppTypeId == IdentifierCatalog.AppTypes.ProxyService);

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
            .CountAsync(a => a.AppTypeId == IdentifierCatalog.AppTypes.ProxyService);

        count.ShouldBe(0);
    }

    [Fact]
    public async Task SeedAsync_SetsCorrectInstallDirectory()
    {
        // Arrange
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var binaryPath = GetKnownExistingBinary();
        var settings = CreateSettings(binaryPath);
        var logger = NullLogger<ProxyAppSeeder>.Instance;
        var seeder = new ProxyAppSeeder(db, settings, logger);

        await RemoveExistingProxyAppsAsync(db);

        // Act
        await seeder.SeedAsync(CancellationToken.None);

        // Assert
        var proxyApp = await db.Apps
            .SingleOrDefaultAsync(a => a.AppTypeId == IdentifierCatalog.AppTypes.ProxyService);

        proxyApp.ShouldNotBeNull();
        proxyApp.InstallDirectory.ShouldBe(Path.GetDirectoryName(binaryPath));
        proxyApp.CommandLine.ShouldNotBeNull();
        File.Exists(proxyApp.CommandLine).ShouldBeTrue();
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
            .Where(a => a.AppTypeId == IdentifierCatalog.AppTypes.ProxyService)
            .ToListAsync();

        db.Apps.RemoveRange(existing);
        await db.SaveChangesAsync();
    }

    private static string GetKnownExistingBinary() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
            : "/bin/sh";
}
