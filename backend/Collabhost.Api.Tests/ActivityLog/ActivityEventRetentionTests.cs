using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.ActivityLog;

// SVC-01: the ActivityEvents table is insert-only with no retention. PruneAsync bounds it on two
// axes (age + count); these tests pin both, plus the disabled-axis escape hatch. RED-first: against
// the pre-fix store (no PruneAsync) the table grows unbounded -- the method itself is the fix, so
// the test is RED by not compiling/finding the behavior until PruneAsync exists, then GREEN once it
// deletes the right rows.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below.
public sealed class ActivityEventRetentionTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ActivityEventStore _store;

    public ActivityEventRetentionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new TestDbContextFactory(_connection);

        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();

        _store = new ActivityEventStore(_dbFactory, NullLogger<ActivityEventStore>.Instance);
    }

    [Fact]
    public async Task PruneAsync_AgeAxis_DeletesRowsOlderThanWindowKeepsRecent()
    {
        var now = DateTime.UtcNow;

        await SeedEventAsync("old", now.AddDays(-100));
        await SeedEventAsync("edge", now.AddDays(-89));
        await SeedEventAsync("recent", now.AddDays(-1));

        var retention = Settings(maxCount: 0, maxAgeDays: 90);

        var removed = await _store.PruneAsync(retention, CancellationToken.None);

        removed.ShouldBe(1);

        var remaining = await RemainingEventTypesAsync();
        remaining.ShouldNotContain("old");
        remaining.ShouldContain("edge");
        remaining.ShouldContain("recent");
    }

    [Fact]
    public async Task PruneAsync_CountAxis_KeepsOnlyTheNewestMaxCountRows()
    {
        // Five events seeded in order with natural (monotonically increasing) ULID Ids -- the exact
        // production shape, where each insert's default Ulid.NewUlid() is greater than the last. The
        // small delay between seeds guarantees the millisecond component advances, so stored-Id order
        // == insertion order == temporal order without relying on same-millisecond monotonicity.
        for (var index = 0; index < 5; index++)
        {
            await SeedEventAsync("e" + index.ToString(CultureInfo.InvariantCulture), DateTime.UtcNow, Ulid.NewUlid());
            await Task.Delay(2);
        }

        var retention = Settings(maxCount: 2, maxAgeDays: 0);

        var removed = await _store.PruneAsync(retention, CancellationToken.None);

        // Keep the newest 2 (e3, e4); delete the oldest 3 (e0, e1, e2).
        removed.ShouldBe(3);

        var remaining = await RemainingEventTypesAsync();
        remaining.Count.ShouldBe(2);
        remaining.ShouldContain("e3");
        remaining.ShouldContain("e4");
    }

    [Fact]
    public async Task PruneAsync_BothAxesDisabled_DeletesNothing()
    {
        await SeedEventAsync("ancient", DateTime.UtcNow.AddYears(-5));

        var retention = Settings(maxCount: 0, maxAgeDays: 0);

        var removed = await _store.PruneAsync(retention, CancellationToken.None);

        removed.ShouldBe(0);
        (await RemainingEventTypesAsync()).ShouldContain("ancient");
    }

    [Fact]
    public async Task PruneAsync_FewerRowsThanMaxCount_DeletesNothingOnCountAxis()
    {
        await SeedEventAsync("a", DateTime.UtcNow);
        await SeedEventAsync("b", DateTime.UtcNow);

        var retention = Settings(maxCount: 50, maxAgeDays: 0);

        var removed = await _store.PruneAsync(retention, CancellationToken.None);

        removed.ShouldBe(0);
        (await RemainingEventTypesAsync()).Count.ShouldBe(2);
    }

    // --- Helpers ---

    private static ActivityEventRetentionSettings Settings(int maxCount, int maxAgeDays) =>
        new()
        {
            MaxCount = maxCount,
            MaxAgeDays = maxAgeDays,
            SweepIntervalMinutes = 60
        };

    private Task SeedEventAsync(string eventType, DateTime timestamp) =>
        SeedEventAsync(eventType, timestamp, Ulid.NewUlid());

    private async Task SeedEventAsync(string eventType, DateTime timestamp, Ulid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        db.ActivityEvents.Add
        (
            new ActivityEvent
            {
                Id = id,
                EventType = eventType,
                ActorId = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
                ActorName = "tester",
                Timestamp = timestamp
            }
        );

        await db.SaveChangesAsync();
    }

    private async Task<List<string>> RemainingEventTypesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.ActivityEvents
            .AsNoTracking()
            .Select(e => e.EventType)
                .ToListAsync();
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => Build(connection);

#pragma warning disable VSTHRD200 // Async method naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
#pragma warning restore VSTHRD200
        Task.FromResult(Build(connection));

    private static AppDbContext Build(SqliteConnection sharedConnection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(sharedConnection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options);
    }
}
