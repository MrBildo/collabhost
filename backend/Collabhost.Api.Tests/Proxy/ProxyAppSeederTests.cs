using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// CaddyResolver owns the full precedence chain as of #153 Phase 2.
// Binary-path resolution tests live in CaddyResolverTests; this file retains only
// high-level guards for ProxyAppSeeder itself. See CaddyResolverTests for the full suite.
public class ProxyAppSeederTests
{
    // Card #196: bare-name PATH walking was removed. A name without a directory
    // separator now fails File.Exists and returns null. Operators routing to a
    // system Caddy must use COLLABHOST_PROXY_BINARY_PATH with an absolute path.
    [Fact]
    public void ResolveBinaryPathSetting_BareName_ReturnsNull() =>
        CaddyResolver.ResolveBinaryPathSetting(OperatingSystem.IsWindows() ? "where" : "sh")
            .ShouldBeNull();

    [Fact]
    public void ResolveBinaryPathSetting_NonexistentPath_ReturnsNull() =>
        CaddyResolver.ResolveBinaryPathSetting("nonexistent-binary-12345").ShouldBeNull();
}

// Atomicity tests for ProxyAppSeeder (#202). Requires a real SQLite database so the
// transaction rollback guarantee can be observed at the storage level.
#pragma warning disable CA1001 // TypeStore is disposed in DisposeAsync -- IAsyncLifetime is the async-lifecycle contract
public class ProxyAppSeederAtomicityTests : IAsyncLifetime
{
    private string _dataDirectory = null!;
    private TestDbContextFactory _dbFactory = null!;
    private AppStore _appStore = null!;
    private TypeStore _typeStore = null!;
    private ActivityEventStore _activityEventStore = null!;
    private string _fakeBinaryPath = null!;

    public async ValueTask InitializeAsync()
    {
        _dataDirectory = Path.Combine
        (
            Path.GetTempPath(),
            $"collabhost-proxyseeder-{Guid.NewGuid():N}"
        );

        Directory.CreateDirectory(_dataDirectory);

        var dbPath = Path.Combine(_dataDirectory, "collabhost.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
                .Options;

        _dbFactory = new TestDbContextFactory(options);

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        var cache = new MemoryCache(new MemoryCacheOptions());

        _appStore = new AppStore(_dbFactory, cache, NullLogger<AppStore>.Instance);

        _typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings
            {
                UserTypesDirectory = Path.Combine(_dataDirectory, "user-types")
            },
            new ProxySettings
            {
                BaseDomain = "collab.internal",
                BinaryPath = null,
                ListenAddress = ":443",
                CertLifetime = "168h"
            },
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await _typeStore.LoadAsync();

        _activityEventStore = new ActivityEventStore
        (
            _dbFactory,
            NullLogger<ActivityEventStore>.Instance
        );

        // Create a real (but empty) file so CaddyResolver.Resolve returns a non-null path.
        // The seeder only needs File.Exists to pass; it does not execute the binary.
        _fakeBinaryPath = Path.Combine(_dataDirectory, "caddy-fake");

        await File.WriteAllBytesAsync(_fakeBinaryPath, []);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        _typeStore?.Dispose();

        if (_dataDirectory is not null && Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        return ValueTask.CompletedTask;
    }

    private ProxyAppSeeder CreateSeeder(string? binaryPath = null) =>
        new
        (
            _appStore,
            _dbFactory,
            _typeStore,
            new ProxySettings
            {
                BaseDomain = "collab.internal",
                BinaryPath = binaryPath ?? _fakeBinaryPath,
                ListenAddress = ":443",
                CertLifetime = "168h"
            },
            _activityEventStore,
            NullLogger<ProxyAppSeeder>.Instance
        );

    private async Task<(int AppCount, int OverrideCount)> GetRowCountsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var appCount = await db.Apps.CountAsync();
        var overrideCount = await db.CapabilityOverrides.CountAsync();

        return (appCount, overrideCount);
    }

    [Fact]
    public async Task SeedAsync_CancelledBeforeCommit_LeavesDbEmpty()
    {
        // A pre-cancelled token simulates a SIGINT arriving during the transaction window.
        // Without the transaction all prior SaveChangesAsync calls would have already
        // committed -- the App row or some CapabilityOverride rows could be left in the DB.
        // With the transaction the DB must be left fully empty after cancellation.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var seeder = CreateSeeder();

        await Should.ThrowAsync<OperationCanceledException>
        (
            () => seeder.SeedAsync(cts.Token)
        );

        var (appCount, overrideCount) = await GetRowCountsAsync();

        appCount.ShouldBe(0);
        overrideCount.ShouldBe(0);
    }

    [Fact]
    public async Task SeedAsync_FullRun_CommitsAppAndAllOverrides()
    {
        // Happy-path baseline: all rows committed, AppStore cache invalidated.
        var seeder = CreateSeeder();

        await seeder.SeedAsync(CancellationToken.None);

        var (appCount, overrideCount) = await GetRowCountsAsync();

        appCount.ShouldBe(1);
        overrideCount.ShouldBe(3); // process, auto-start, artifact

        // AppStore should reflect the seeded proxy after cache invalidation
        var proxy = await _appStore.GetBySlugAsync("proxy", CancellationToken.None);

        proxy.ShouldNotBeNull();
        proxy!.Slug.ShouldBe("proxy");
    }
}
#pragma warning restore CA1001

// Sealed: test helper, no subtype needed.
internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

#pragma warning disable VSTHRD200 // Interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(options));
#pragma warning restore VSTHRD200
}
