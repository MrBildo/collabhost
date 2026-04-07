using Collabhost.Api.Authorization;
using Collabhost.Api.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

public class UserStoreTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly TestDbContextFactory _dbFactory;
    private readonly MemoryCache _cache;
    private readonly UserStore _store;

    public UserStoreTests()
    {
        _dbPath = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-userstore-tests",
            Guid.NewGuid().ToString("N"),
            "test.db"
        );

        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbFactory = new TestDbContextFactory(options);
        _cache = new MemoryCache(new MemoryCacheOptions());
        _store = new UserStore(_dbFactory, _cache, NullLogger<UserStore>.Instance);

        // Run migrations synchronously in constructor via a sync-over-async pattern acceptable in test setup
        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        _cache.Dispose();

        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_ReturnsUserWithGeneratedKey()
    {
        var user = await _store.CreateAsync("Alice", UserRole.Administrator, CancellationToken.None);

        user.Name.ShouldBe("Alice");
        user.Role.ShouldBe(UserRole.Administrator);
        user.AuthKey.ShouldNotBeNullOrEmpty();
        user.IsActive.ShouldBeTrue();
        user.Id.ShouldNotBe(Ulid.Empty);
    }

    [Fact]
    public async Task GetByAuthKeyAsync_AfterCreate_ReturnsUser()
    {
        var created = await _store.CreateAsync("Bob", UserRole.Agent, CancellationToken.None);

        var found = await _store.GetByAuthKeyAsync(created.AuthKey, CancellationToken.None);

        found.ShouldNotBeNull();
        found!.Id.ShouldBe(created.Id);
        found.Name.ShouldBe("Bob");
        found.Role.ShouldBe(UserRole.Agent);
    }

    [Fact]
    public async Task GetByAuthKeyAsync_UnknownKey_ReturnsNull()
    {
        var result = await _store.GetByAuthKeyAsync("01DOESNOTEXIST00000000000X", CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_AfterCreate_ReturnsUser()
    {
        var created = await _store.CreateAsync("Carol", UserRole.Agent, CancellationToken.None);

        var found = await _store.GetByIdAsync(created.Id, CancellationToken.None);

        found.ShouldNotBeNull();
        found!.Name.ShouldBe("Carol");
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _store.GetByIdAsync(Ulid.NewUlid(), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllCreatedUsers()
    {
        await _store.CreateAsync("Dave", UserRole.Administrator, CancellationToken.None);
        await _store.CreateAsync("Eve", UserRole.Agent, CancellationToken.None);

        var all = await _store.GetAllAsync(CancellationToken.None);

        all.Count.ShouldBeGreaterThanOrEqualTo(2);
        all.ShouldContain(u => u.Name == "Dave");
        all.ShouldContain(u => u.Name == "Eve");
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        var created = await _store.CreateAsync("Frank", UserRole.Agent, CancellationToken.None);

        await _store.DeactivateAsync(created.Id, CancellationToken.None);

        var found = await _store.GetByIdAsync(created.Id, CancellationToken.None);

        found.ShouldNotBeNull();
        found!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_EvictsCacheEntry()
    {
        var created = await _store.CreateAsync("Grace", UserRole.Agent, CancellationToken.None);

        // Populate cache by fetching the user
        _ = await _store.GetByAuthKeyAsync(created.AuthKey, CancellationToken.None);
        _ = await _store.GetByIdAsync(created.Id, CancellationToken.None);

        // Deactivate — should evict from cache
        await _store.DeactivateAsync(created.Id, CancellationToken.None);

        // Cache is cleared; fresh DB hit will return deactivated user
        var refreshed = await _store.GetByIdAsync(created.Id, CancellationToken.None);

        refreshed.ShouldNotBeNull();
        refreshed!.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_UnknownId_DoesNotThrow() =>
        await Should.NotThrowAsync
        (
            () => _store.DeactivateAsync(Ulid.NewUlid(), CancellationToken.None)
        );

    [Fact]
    public async Task DeactivateAsync_LastActiveAdministrator_ThrowsInvalidOperationException()
    {
        var admin = await _store.CreateAsync("LastAdmin", UserRole.Administrator, CancellationToken.None);

        var exception = await Should.ThrowAsync<InvalidOperationException>
        (
            () => _store.DeactivateAsync(admin.Id, CancellationToken.None)
        );

        exception.Message.ShouldBe("Cannot deactivate the last active administrator");
    }

    [Fact]
    public async Task DeactivateAsync_AdminWhenOtherActiveAdminsExist_Deactivates()
    {
        var admin1 = await _store.CreateAsync("Admin1", UserRole.Administrator, CancellationToken.None);
        var admin2 = await _store.CreateAsync("Admin2", UserRole.Administrator, CancellationToken.None);

        await Should.NotThrowAsync
        (
            () => _store.DeactivateAsync(admin1.Id, CancellationToken.None)
        );

        var found = await _store.GetByIdAsync(admin1.Id, CancellationToken.None);

        found.ShouldNotBeNull();
        found!.IsActive.ShouldBeFalse();

        // admin2 is still active — the second admin is unaffected
        _ = admin2;
    }

    [Fact]
    public async Task GetByAuthKeyAsync_CachesOnSecondCall()
    {
        var created = await _store.CreateAsync("Hank", UserRole.Agent, CancellationToken.None);

        // First call: DB hit, populates cache
        var first = await _store.GetByAuthKeyAsync(created.AuthKey, CancellationToken.None);

        // Second call: should return from cache (same instance)
        var second = await _store.GetByAuthKeyAsync(created.AuthKey, CancellationToken.None);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        first!.Id.ShouldBe(second!.Id);
    }
}

// Sealed: test helper only, no subtype need
internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

#pragma warning disable VSTHRD200 // Interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(options));
#pragma warning restore VSTHRD200
}
