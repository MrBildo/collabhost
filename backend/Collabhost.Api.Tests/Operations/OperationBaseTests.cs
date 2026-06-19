using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data;
using Collabhost.Api.Operations;
using Collabhost.Api.Registry;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Operations;

// Unit tests for the Operation<,> base (code-structure-conventions §8) -- the two plumbing
// strands the base hoists once for every leaf. First strand: the InvalidOperationException
// catch that maps a bad state transition to OperationResult.Conflict. Second strand: RecordAsync,
// the actor-stamped activity-event helper.
// The base is exercised through TestOperation, a trivial concrete subclass that lets each test
// drive ExecuteCoreAsync's outcome and reach the protected RecordAsync overloads. The recorder
// is backed by a real ActivityEventStore over an in-memory SQLite db, so the persisted event is
// the oracle for the actor + app stamping.
public sealed class OperationBaseTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public OperationBaseTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using (var db = CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _activityEventStore = new ActivityEventStore
        (
            new InMemoryDbContextFactory(_connection),
            NullLogger<ActivityEventStore>.Instance
        );

        _actor = new User
        {
            Name = "Test Actor",
            AuthKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
            Role = UserRole.Administrator
        };

        _currentUser = new CurrentUser();
        _currentUser.Set(_actor);
    }

    // 1. try/catch -> Conflict hoist: an InvalidOperationException thrown by the leaf body is
    // caught by the base and surfaced as a Conflict carrying the exception message. This is the
    // hoist of the catch block repeated across every lifecycle handler on both surfaces.
    [Fact]
    public async Task ExecuteAsync_WhenCoreThrowsInvalidOperationException_ReturnsConflictWithMessage()
    {
        var operation = new TestOperation(_currentUser, _activityEventStore)
        {
            Core = (_, _) => throw new InvalidOperationException("bad transition")
        };

        var result = await operation.ExecuteAsync("anything", CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Conflict);
        result.Error.ShouldBe("bad transition");
    }

    // The happy path passes straight through the base untouched: a Success from the core is the
    // Success the caller sees, value intact.
    [Fact]
    public async Task ExecuteAsync_WhenCoreSucceeds_PassesSuccessThrough()
    {
        var operation = new TestOperation(_currentUser, _activityEventStore)
        {
            Core = (command, _) => Task.FromResult(OperationResult<string>.Success(command + "-done"))
        };

        var result = await operation.ExecuteAsync("work", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("work-done");
    }

    // The leaf's OWN typed failures (NotFound/Validation it returns explicitly) pass through the
    // base unchanged -- the base only intercepts the bubbled InvalidOperationException, never a
    // failure the leaf chose to return.
    [Fact]
    public async Task ExecuteAsync_WhenCoreReturnsNotFound_PassesNotFoundThrough()
    {
        var operation = new TestOperation(_currentUser, _activityEventStore)
        {
            Core = (_, _) => Task.FromResult(OperationResult<string>.NotFound("app 'x' not found"))
        };

        var result = await operation.ExecuteAsync("x", CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("app 'x' not found");
    }

    // A non-InvalidOperationException is NOT swallowed by the base -- only the bad-state-transition
    // exception maps to Conflict; anything else bubbles, so a genuine bug stays loud.
    [Fact]
    public async Task ExecuteAsync_WhenCoreThrowsOtherException_DoesNotSwallow()
    {
        var operation = new TestOperation(_currentUser, _activityEventStore)
        {
            Core = (_, _) => throw new TimeoutException("downstream timed out")
        };

        await Should.ThrowAsync<TimeoutException>
        (
            () => operation.ExecuteAsync("anything", CancellationToken.None)
        );
    }

    // 2. RecordAsync(eventType, app, ct): the App overload stamps the acting user (id + name) and
    // the app (id + slug) onto the persisted event, with no metadata. This is the hoist that
    // deletes the hand-built six-field ActivityEvent from every leaf body.
    [Fact]
    public async Task RecordAsync_WithApp_StampsActorAndApp()
    {
        var app = new App
        {
            Slug = "my-app",
            DisplayName = "My App",
            AppTypeSlug = "dotnet-app"
        };

        var operation = new TestOperation(_currentUser, _activityEventStore);

        await operation.RecordEventAsync(ActivityEventTypes.AppStarted, app, CancellationToken.None);

        var recorded = await SingleRecordedEventAsync();

        recorded.EventType.ShouldBe(ActivityEventTypes.AppStarted);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe("Test Actor");
        recorded.AppId.ShouldBe(app.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.AppSlug.ShouldBe("my-app");
        recorded.MetadataJson.ShouldBeNull();
    }

    // RecordAsync(eventType, appId, appSlug, metadataJson, ct): the string overload stamps the
    // acting user and carries the raw app id + slug + metadata -- the shape the delete operation
    // needs (the entity row is gone before app.deleted is emitted) and the create operation needs
    // (it carries display-name metadata).
    [Fact]
    public async Task RecordAsync_WithIdsAndMetadata_StampsActorIdsAndMetadata()
    {
        var operation = new TestOperation(_currentUser, _activityEventStore);

        await operation.RecordEventAsync
        (
            ActivityEventTypes.AppDeleted,
            "01HXXXXXXXXXXXXXXXXXXXXXXX",
            "gone-app",
            "{\"displayName\":\"Gone App\"}",
            CancellationToken.None
        );

        var recorded = await SingleRecordedEventAsync();

        recorded.EventType.ShouldBe(ActivityEventTypes.AppDeleted);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe("Test Actor");
        recorded.AppId.ShouldBe("01HXXXXXXXXXXXXXXXXXXXXXXX");
        recorded.AppSlug.ShouldBe("gone-app");
        recorded.MetadataJson.ShouldBe("{\"displayName\":\"Gone App\"}");
    }

    private async Task<ActivityEvent> SingleRecordedEventAsync()
    {
        await using var db = CreateDbContext();

        return await db.ActivityEvents.SingleAsync();
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}

// A trivial concrete operation that exposes the base's protected surface to the tests: Core is a
// swappable ExecuteCoreAsync body, and the RecordEventAsync methods forward to the base's
// protected RecordAsync overloads so the tests can drive the recorder directly.
file sealed class TestOperation
(
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<string, string>(currentUser, activityEventStore)
{
    public Func<string, CancellationToken, Task<OperationResult<string>>> Core { get; init; }
        = (command, _) => Task.FromResult(OperationResult<string>.Success(command));

    protected override Task<OperationResult<string>> ExecuteCoreAsync(string command, CancellationToken ct) =>
        Core(command, ct);

    public Task RecordEventAsync(string eventType, App app, CancellationToken ct) =>
        RecordAsync(eventType, app, ct);

    public Task RecordEventAsync
    (
        string eventType,
        string appId,
        string appSlug,
        string? metadataJson,
        CancellationToken ct
    ) =>
        RecordAsync(eventType, appId, appSlug, metadataJson, ct);
}

file sealed class InMemoryDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new AppDbContext(options);
    }

#pragma warning disable VSTHRD200 // Async naming -- interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
#pragma warning restore VSTHRD200
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return Task.FromResult(new AppDbContext(options));
    }
}
