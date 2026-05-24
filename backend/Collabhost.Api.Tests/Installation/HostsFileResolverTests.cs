using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Installation;
using Collabhost.Api.Registry;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Installation;

public class HostsFileResolverTests : IAsyncLifetime
{
    private string _scratchDir = string.Empty;
    private string _dataDir = string.Empty;
    private string _dbPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"collabhost-resolver-{Guid.NewGuid():N}");
        _dataDir = Path.Combine(_scratchDir, "data");
        Directory.CreateDirectory(_dataDir);
        _dbPath = Path.Combine(_dataDir, "collabhost.db");

        // Build a real SQLite file at the path the resolver expects.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        // Force connection-pool drain so the .db file can be removed.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (Directory.Exists(_scratchDir))
        {
            try
            {
                Directory.Delete(_scratchDir, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Resolve_NoApps_ReturnsPortalHostnameOnly()
    {
        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.Entries.Count.ShouldBe(1);
        resolved.Entries[0].Hostname.ShouldBe("collabhost.collab.internal");
        resolved.Entries[0].IpAddress.ShouldBe("127.0.0.1");
        resolved.CollisionWarnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolve_NoDbFile_ReturnsPortalHostnameOnly()
    {
        // Point at a fresh data dir with no DB. Resolver should still emit the Portal hostname so
        // a pre-first-boot operator can stage hosts before starting Collabhost.
        var freshDir = Path.Combine(_scratchDir, "no-db");
        Directory.CreateDirectory(freshDir);

        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, freshDir, CancellationToken.None);

        resolved.Entries.Count.ShouldBe(1);
        resolved.Entries[0].Hostname.ShouldBe("collabhost.collab.internal");
    }

    [Fact]
    public async Task Resolve_ThreeAppsRegistered_EmitsPortalPlusPerAppSortedAlphabetically()
    {
        await SeedAppsAsync("zebra-app", "alpha-app", "myapp");

        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.Entries.Count.ShouldBe(4);

        // Sorted alphabetically by hostname.
        resolved.Entries[0].Hostname.ShouldBe("alpha-app.collab.internal");
        resolved.Entries[1].Hostname.ShouldBe("collabhost.collab.internal");
        resolved.Entries[2].Hostname.ShouldBe("myapp.collab.internal");
        resolved.Entries[3].Hostname.ShouldBe("zebra-app.collab.internal");
    }

    [Fact]
    public async Task Resolve_HonorsPortalSubdomainEnvOverride()
    {
        await SeedAppsAsync("myapp");

        var config = BuildConfig
        (
            extraSettings: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Portal:Subdomain"] = "dashboard"
            }
        );

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.Entries.ShouldContain(e => e.Hostname == "dashboard.collab.internal");
    }

    [Fact]
    public async Task Resolve_HonorsProxyBaseDomainOverride()
    {
        await SeedAppsAsync("myapp");

        var config = BuildConfig
        (
            extraSettings: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Proxy:BaseDomain"] = "lan.example"
            }
        );

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.Entries.ShouldContain(e => e.Hostname == "collabhost.lan.example");
        resolved.Entries.ShouldContain(e => e.Hostname == "myapp.lan.example");
    }

    [Fact]
    public async Task Resolve_HonorsPerAppDomainPatternOverride()
    {
        await SeedAppsAsync("myapp");

        // Add a routing override that pins the app to a custom hostname.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        await using (var db = new AppDbContext(options))
        {
            var app = await db.Apps.SingleAsync(a => a.Slug == "myapp");

            db.CapabilityOverrides.Add
            (
                new CapabilityOverride
                {
                    AppId = app.Id,
                    CapabilitySlug = "routing",
                    ConfigurationJson = """{"domainPattern":"custom.example.com"}"""
                }
            );

            await db.SaveChangesAsync();
        }

        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.Entries.ShouldContain(e => e.Hostname == "custom.example.com");
    }

    [Fact]
    public async Task Resolve_DuplicateHostnames_EmitsCollisionWarning()
    {
        await SeedAppsAsync("myapp");

        // Override myapp's domain pattern so it collides with the Portal hostname.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        await using (var db = new AppDbContext(options))
        {
            var app = await db.Apps.SingleAsync(a => a.Slug == "myapp");

            db.CapabilityOverrides.Add
            (
                new CapabilityOverride
                {
                    AppId = app.Id,
                    CapabilitySlug = "routing",
                    ConfigurationJson = """{"domainPattern":"collabhost.collab.internal"}"""
                }
            );

            await db.SaveChangesAsync();
        }

        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        resolved.CollisionWarnings.ShouldNotBeEmpty();
        resolved.CollisionWarnings[0].ShouldContain("collabhost.collab.internal");
    }

    [Fact]
    public async Task Resolve_CorruptOverrideJson_FallsBackToDefaultPattern()
    {
        await SeedAppsAsync("myapp");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        await using (var db = new AppDbContext(options))
        {
            var app = await db.Apps.SingleAsync(a => a.Slug == "myapp");

            db.CapabilityOverrides.Add
            (
                new CapabilityOverride
                {
                    AppId = app.Id,
                    CapabilitySlug = "routing",
                    ConfigurationJson = "{not valid json"
                }
            );

            await db.SaveChangesAsync();
        }

        var config = BuildConfig();

        var resolved = await HostsFileResolver.ResolveAsync(config, _dataDir, CancellationToken.None);

        // Fall back to default pattern -- the app still gets a hosts entry.
        resolved.Entries.ShouldContain(e => e.Hostname == "myapp.collab.internal");
    }

    private async Task SeedAppsAsync(params string[] slugs)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        await using var db = new AppDbContext(options);

        foreach (var slug in slugs)
        {
            db.Apps.Add(new App
            {
                Slug = slug,
                DisplayName = slug,
                AppTypeSlug = "static-site"
            });
        }

        await db.SaveChangesAsync();
    }

    private static IConfiguration BuildConfig(IReadOnlyDictionary<string, string?>? extraSettings = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

        if (extraSettings is not null)
        {
            foreach (var (k, v) in extraSettings)
            {
                settings[k] = v;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
                .Build();
    }
}
