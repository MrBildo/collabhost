using Collabhost.Api.Data;
using Collabhost.Api.Registry;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Registry;

// REG-02 / REG-04 coverage for AppStore.CreateWithOverridesAsync -- the transactional create that
// replaced the non-atomic CreateAsync + SaveOverrideAsync loop in the registration path.
//
//   REG-02 (atomicity): the App row and all its capability overrides commit together or not at all.
//     The pre-fix shape committed the App in its own context, then each override in its own context,
//     so a failure mid-loop left a half-configured app persisted. These tests drive a duplicate-slug
//     collision DURING the single transaction and assert NOTHING leaks -- no app row added, no
//     override rows added.
//
//   REG-04 (409 not 500): the unique-Slug-index collision surfaces as DbUpdateException from the
//     single SaveChangesAsync. CreateAppOperation catches that and maps it to a Conflict (409); these
//     tests pin the exception TYPE the operation's catch depends on.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class AppStoreCreateTransactionTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _store;

    public AppStoreCreateTransactionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new SharedConnectionDbContextFactory(_connection);

        using (var db = _dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _store = new AppStore
        (
            _dbFactory,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AppStore>.Instance
        );
    }

    [Fact]
    public async Task CreateWithOverrides_HappyPath_PersistsAppAndAllOverrides()
    {
        var app = new App { Slug = "alpha", DisplayName = "Alpha", AppTypeSlug = "static-site" };

        await _store.CreateWithOverridesAsync
        (
            app,
            [("routing", "{\"domainPattern\":\"alpha.collab.internal\"}"), ("artifact", "{\"location\":\"/srv/alpha\"}")],
            CancellationToken.None
        );

        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.Apps.CountAsync()).ShouldBe(1);

        var overrides = await db.CapabilityOverrides
            .Where(o => o.AppId == app.Id)
                .ToListAsync();

        overrides.Count.ShouldBe(2);
        overrides.Select(o => o.CapabilitySlug).ShouldBe(["routing", "artifact"], ignoreOrder: true);
    }

    [Fact]
    public async Task CreateWithOverrides_DuplicateSlug_ThrowsDbUpdateException()
    {
        await SeedRawAppAsync("dupe", "static-site");

        var colliding = new App { Slug = "dupe", DisplayName = "Dupe 2", AppTypeSlug = "static-site" };

        // REG-04: the unique Slug index rejects the insert; the throw is DbUpdateException -- the
        // exception type CreateAppOperation's catch keys on to return Conflict instead of 500.
        await Should.ThrowAsync<DbUpdateException>(async () =>
            await _store.CreateWithOverridesAsync
            (
                colliding,
                [("routing", "{\"domainPattern\":\"dupe.collab.internal\"}")],
                CancellationToken.None
            ));
    }

    [Fact]
    public async Task CreateWithOverrides_OverrideSaveFailsMidLoop_RollsBackAppAndAllOverrides()
    {
        // REG-02 atomicity, the faithful half-write: a UNIQUE (AppId, CapabilitySlug) index means two
        // overrides with the SAME slug collide on save -- and crucially that collision lands AFTER the
        // App row would have committed under the pre-fix shape. Pre-fix (CreateAsync commits the App in
        // its own context, then each SaveOverrideAsync commits in its own context), the App + the first
        // override are already on disk when the second override throws -> a half-configured app
        // persisted. Post-fix (one transaction), the App add + both override adds share a single
        // SaveChangesAsync; the unique-index violation rolls the whole thing back -> NOTHING persists.
        var app = new App { Slug = "gamma", DisplayName = "Gamma", AppTypeSlug = "static-site" };

        await Should.ThrowAsync<DbUpdateException>(async () =>
            await _store.CreateWithOverridesAsync
            (
                app,
                [("routing", "{\"first\":1}"), ("routing", "{\"second\":2}")],
                CancellationToken.None
            ));

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Atomic rollback: no app row, no override rows from the failed create.
        (await db.Apps.CountAsync(a => a.Id == app.Id)).ShouldBe(0);
        (await db.CapabilityOverrides.CountAsync(o => o.AppId == app.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task CreateWithOverrides_DuplicateSlug_RollsBack_OriginalUntouched()
    {
        var original = await SeedRawAppAsync("beta", "static-site");

        var colliding = new App { Slug = "beta", DisplayName = "Beta 2", AppTypeSlug = "static-site" };

        await Should.ThrowAsync<DbUpdateException>(async () =>
            await _store.CreateWithOverridesAsync
            (
                colliding,
                [("routing", "{\"x\":1}")],
                CancellationToken.None
            ));

        await using var db = await _dbFactory.CreateDbContextAsync();

        // The duplicate-slug insert fails at the App row itself; still exactly the one seeded app and
        // no leaked override rows. (REG-04: the throw is DbUpdateException, asserted above.)
        (await db.Apps.CountAsync()).ShouldBe(1);
        (await db.CapabilityOverrides.CountAsync(o => o.AppId == colliding.Id)).ShouldBe(0);
        (await db.Apps.SingleAsync()).Id.ShouldBe(original.Id);
    }

    private async Task<App> SeedRawAppAsync(string slug, string appTypeSlug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var app = new App { Slug = slug, DisplayName = "Seed", AppTypeSlug = appTypeSlug };

        db.Apps.Add(app);
        await db.SaveChangesAsync();

        return app;
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class SharedConnectionDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => Build(connection);

#pragma warning disable VSTHRD200 // Async naming -- interface implementation
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
