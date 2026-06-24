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

// #432: QueryAsync keyset cursor pagination used string.CompareTo, which does NOT translate to SQL
// on EF Core 10 / SQLite -- so any request carrying a cursor (every page past the first) threw
// InvalidOperationException and GET /api/v1/events?cursor=... returned 500. No test exercised the
// cursor path, so the bug was invisible. These tests run against the real AppDbContext + in-memory
// SQLite (the production provider, with the Ulid<->string value converter), so the translation
// failure surfaces exactly as it does in production: RED against the CompareTo predicate, GREEN once
// the predicate is a translatable relational comparison.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below.
public sealed class ActivityEventQueryCursorTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ActivityEventStore _store;

    public ActivityEventQueryCursorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new CursorTestDbContextFactory(_connection);

        using var db = _dbFactory.CreateDbContext();
        db.Database.EnsureCreated();

        _store = new ActivityEventStore(_dbFactory, NullLogger<ActivityEventStore>.Instance);
    }

    [Fact]
    public async Task QueryAsync_SecondPageWithCursor_DoesNotThrowAndReturnsNextPage()
    {
        await SeedOrderedEventsAsync(5);

        var firstPage = await _store.QueryAsync(Query(limit: 2), CancellationToken.None);

        firstPage.Items.Count.ShouldBe(2);
        firstPage.HasMore.ShouldBeTrue();
        firstPage.NextCursor.ShouldNotBeNull();

        // The bug: this second call carries a cursor, so it hits the keyset predicate. Against the
        // pre-fix string.CompareTo code it throws InvalidOperationException ("Translation of method
        // 'string.CompareTo' failed"); against the fix it returns the next page.
        var secondPage = await _store.QueryAsync(Query(limit: 2, cursor: firstPage.NextCursor), CancellationToken.None);

        secondPage.Items.Count.ShouldBe(2);

        var firstPageTypes = firstPage.Items.Select(e => e.EventType).ToList();
        var secondPageTypes = secondPage.Items.Select(e => e.EventType).ToList();

        // Newest-first: page 1 is e4,e3; page 2 is e2,e1; no overlap.
        firstPageTypes.ShouldBe(["e4", "e3"]);
        secondPageTypes.ShouldBe(["e2", "e1"]);
        secondPageTypes.ShouldNotContain(type => firstPageTypes.Contains(type));
    }

    [Fact]
    public async Task QueryAsync_PagingToEnd_TerminatesWithoutThrowing()
    {
        await SeedOrderedEventsAsync(5);

        var collected = new List<string>();
        string? cursor = null;
        var pageCount = 0;

        do
        {
            var page = await _store.QueryAsync(Query(limit: 2, cursor: cursor), CancellationToken.None);

            collected.AddRange(page.Items.Select(e => e.EventType));
            cursor = page.NextCursor;
            pageCount++;

            // Guard against an infinite loop if the keyset predicate ever stops advancing.
            pageCount.ShouldBeLessThanOrEqualTo(5);
        }
        while (cursor is not null);

        // Three pages (2 + 2 + 1), every event seen exactly once, strict newest-first order.
        pageCount.ShouldBe(3);
        collected.ShouldBe(["e4", "e3", "e2", "e1", "e0"]);
    }

    [Fact]
    public async Task QueryAsync_MalformedCursor_FallsBackToFirstPageWithoutThrowing()
    {
        await SeedOrderedEventsAsync(3);

        // The cursor is untrusted client input. A non-ULID string fails the parse and is ignored
        // (first page returned) rather than throwing -- keeps the feed resilient against a mangled
        // cursor instead of returning a 500.
        var page = await _store.QueryAsync(Query(limit: 2, cursor: "not-a-ulid"), CancellationToken.None);

        page.Items.Count.ShouldBe(2);
        page.Items.Select(e => e.EventType).ShouldBe(["e2", "e1"]);
    }

    // --- Helpers ---

    private static ActivityEventQuery Query(int limit, string? cursor = null) =>
        new
        (
            Category: null,
            AppSlug: null,
            ActorId: null,
            EventType: null,
            Since: null,
            Until: null,
            Limit: limit,
            Cursor: cursor
        );

    // Five events seeded in insertion order with natural (monotonically increasing) ULID Ids -- the
    // production shape, where each insert's default Ulid.NewUlid() is greater than the last. The small
    // delay guarantees the millisecond component advances, so stored-Id order == insertion order ==
    // newest-first when ordered descending. e0 is oldest, e(count-1) is newest.
    private async Task SeedOrderedEventsAsync(int count)
    {
        for (var index = 0; index < count; index++)
        {
            await SeedEventAsync("e" + index.ToString(CultureInfo.InvariantCulture));
            await Task.Delay(2);
        }
    }

    private async Task SeedEventAsync(string eventType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        db.ActivityEvents.Add
        (
            new ActivityEvent
            {
                Id = Ulid.NewUlid(),
                EventType = eventType,
                ActorId = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
                ActorName = "tester",
                Timestamp = DateTime.UtcNow
            }
        );

        await db.SaveChangesAsync();
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class CursorTestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
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
