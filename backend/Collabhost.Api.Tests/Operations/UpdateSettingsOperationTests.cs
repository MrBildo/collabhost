using System.Globalization;
using System.Text.Json.Nodes;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Operations;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Supervisor.Containment;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Operations;

// Operation-level unit tests for UpdateSettingsOperation (#406 spine PR 5, code-structure-conventions
// §8/§9). These drive operation.ExecuteAsync DIRECTLY against a real AppStore + ActivityEventStore +
// CapabilityStore + ProxyManager + ProbeService + RuntimeConfigFileWriter over in-memory SQLite, plus
// a CurrentUser the test sets so the base's actor stamp is exercised.
//
// What is driven directly here (and why):
//
//   - NotFound: the leaf's explicit OperationResult.NotFound when the slug is absent. Nothing is
//     touched and no activity event is recorded.
//
//   - The routing-only settings-save end-to-end (a static-site -- has routing, no process). The save
//     loop: validate-edits -> merge -> SaveOverrideAsync, then invalidate, conditional render, and the
//     actor-stamped app.settings_updated event. Deterministic without a live process.
//
//   - THE NOVEL SHAPE -- the partial-success conflict-with-value path. A runtime-config-file change
//     with the route enabled and a writer that FAILS (the override carries an invalid Path, set at
//     seed time when the locked field is editable) must: (a) ALREADY have persisted the override, (b)
//     return OperationResult.Conflict with the exact "Settings saved, but failed to write runtime-
//     config file: ..." prefix both surfaces returned, (c) carry NO outcome value, (d) record NO
//     activity event (the pre-migration code returned before the event on this path). This is the
//     conflict-carrying-a-value the 3-kind model represents faithfully as Conflict + no value + the
//     prefix-bearing message.
//
//   - Validation failure (an unknown FIELD in a known section) -> Validation, no save, no event.
//
//   - The RejectUnknownSection flag divergence: an unknown SECTION is rejected mid-loop (MCP, true) or
//     skipped (REST, false). Both preserve the per-surface pre-migration behavior.
//
//   - The ValidateMergedOverrides flag is exercised on the happy path (REST passes true) -- a clean
//     merged state passes; the flag's drift semantics are documented in _OperationContracts.cs.
//
// The live process branch has no settings-specific surface here (settings updates do not start a
// process); the route-level REST integration + MCP transport tests (RuntimeConfigFileTriggerTests on
// both surfaces) are the dual oracle for the render-trigger and the full HTTP/transport behavior.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class UpdateSettingsOperationTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AppStore _appStore;
    private readonly ActivityEventStore _activityEventStore;
    private readonly CurrentUser _currentUser;
    private readonly User _actor;

    public UpdateSettingsOperationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbFactory = new TestDbContextFactory(_connection);

        using (var db = _dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        _appStore = new AppStore(_dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);
        _activityEventStore = new ActivityEventStore(_dbFactory, NullLogger<ActivityEventStore>.Instance);

        _actor = new User
        {
            Name = "Test Actor",
            AuthKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
            Role = UserRole.Administrator
        };

        _currentUser = new CurrentUser();
        _currentUser.Set(_actor);
    }

    [Fact]
    public async Task UnknownSlug_ReturnsNotFoundAndRecordsNoEvent()
    {
        var (operation, _) = await CreateOperationAsync();

        var command = new UpdateSettingsCommand
        (
            "no-such-app",
            [],
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.NotFound);
        result.Error.ShouldBe("App 'no-such-app' not found.");

        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task RoutingOnly_ValidChange_SavesOverride_RecordsEvent_ReturnsSlug()
    {
        var app = await SeedStaticSiteAsync("site-a");

        var (operation, _) = await CreateOperationAsync();

        // A valid runtime-config-file values edit (route NOT enabled here, so the render gate does
        // not fire -- this isolates the save + event path from the render path).
        var command = new UpdateSettingsCommand
        (
            "site-a",
            ChangesFor("runtime-config-file", ("values", new JsonObject { ["apiBaseUrl"] = "https://a.example/api" })),
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Slug.ShouldBe("site-a");

        var overrides = await _appStore.GetOverridesAsync(app.Id, CancellationToken.None);
        overrides.ShouldContainKey("runtime-config-file");
        overrides["runtime-config-file"].ConfigurationJson.ShouldContain("apiBaseUrl");

        await AssertEventRecordedAsync(ActivityEventTypes.AppSettingsUpdated, "site-a");
    }

    [Fact]
    public async Task IdentityChange_UpdatesDisplayName_RecordsEvent()
    {
        await SeedStaticSiteAsync("site-id");

        var (operation, _) = await CreateOperationAsync();

        var command = new UpdateSettingsCommand
        (
            "site-id",
            ChangesFor("identity", ("displayName", "Renamed Site")),
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var refreshed = await _appStore.GetBySlugAsync("site-id", CancellationToken.None);
        refreshed!.DisplayName.ShouldBe("Renamed Site");

        // The event's changedCapabilities excludes "identity" (matches pre-migration metadata shape).
        await AssertEventRecordedAsync(ActivityEventTypes.AppSettingsUpdated, "site-id");
    }

    // THE NOVEL SHAPE: settings persist, the runtime-config-file write then fails -> Conflict with the
    // exact prefix AND the override IS saved AND no event recorded AND no value.
    [Fact]
    public async Task PartialSuccess_RenderFails_PersistsOverride_ReturnsConflict_NoValue_NoEvent()
    {
        var app = await SeedStaticSiteAsync("site-partial");

        // Seed an invalid Path on the runtime-config-file override (path is a registration-locked
        // field, so it can only be set at seed time -- editing it via the operation would be rejected
        // by ValidateEdits). Non-empty Values so the writer does NOT short-circuit and reaches the
        // ValidatePath check, which fails on the "../escape" traversal segment.
        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            new JsonObject
            {
                ["path"] = "../escape",
                ["values"] = new JsonObject { ["apiBaseUrl"] = "https://seed.example/api" }
            }.ToJsonString(),
            CancellationToken.None
        );

        _appStore.InvalidateOverrides(app.Id);

        var (operation, proxy) = await CreateOperationAsync();

        // The route must be enabled for the render gate (IsRouteEnabled) to fire.
        proxy.EnableRoute("site-partial");

        // A runtime-config-file values edit -- triggers the save (which succeeds) then the render
        // (which fails on the invalid seeded path).
        var command = new UpdateSettingsCommand
        (
            "site-partial",
            ChangesFor("runtime-config-file", ("values", new JsonObject { ["apiBaseUrl"] = "https://changed.example/api" })),
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        // Conflict-carrying-a-value: the 3-kind model represents it as Conflict + no value + prefix.
        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Conflict);
        result.Value.ShouldBeNull();
        result.Error.ShouldNotBeNull();
        result.Error!.ShouldStartWith("Settings saved, but failed to write runtime-config file: ");

        // The settings DID persist before the render failed (the "settings saved" half of the
        // conflict-with-value): the operator's new value is in the stored override.
        var overrides = await _appStore.GetOverridesAsync(app.Id, CancellationToken.None);
        overrides.ShouldContainKey("runtime-config-file");
        overrides["runtime-config-file"].ConfigurationJson.ShouldContain("changed.example");

        // No activity event recorded on the partial-success path (pre-migration both surfaces returned
        // the conflict BEFORE the event record).
        await AssertNoEventRecordedAsync();
    }

    [Fact]
    public async Task ValidationFailure_UnknownField_ReturnsValidation_NoSave_NoEvent()
    {
        var app = await SeedStaticSiteAsync("site-val");

        var (operation, _) = await CreateOperationAsync();

        // An unknown FIELD in a known section -> ValidateEdits returns errors -> Validation.
        var command = new UpdateSettingsCommand
        (
            "site-val",
            ChangesFor("runtime-config-file", ("notAField", "x")),
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Validation);
        result.Error.ShouldNotBeNullOrEmpty();

        // No override saved for this section (the validation failure returned before SaveOverride).
        var overrides = await _appStore.GetOverridesAsync(app.Id, CancellationToken.None);
        overrides.ShouldNotContainKey("runtime-config-file");

        await AssertNoEventRecordedAsync();
    }

    // The MCP path (RejectUnknownSection true) rejects an unknown section with the exact pre-migration
    // message; the operation builds it surface-agnostic and the MCP adapter passes it through verbatim.
    [Fact]
    public async Task UnknownSection_RejectTrue_ReturnsValidationWithMessage()
    {
        await SeedStaticSiteAsync("site-mcp");

        var (operation, _) = await CreateOperationAsync();

        var command = new UpdateSettingsCommand
        (
            "site-mcp",
            ChangesFor("not-a-capability", ("foo", "bar")),
            ValidateMergedOverrides: false,
            RefreshProbesOnArtifactChange: false,
            RejectUnknownSection: true
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.FailureKind.ShouldBe(OperationFailureKind.Validation);
        result.Error.ShouldBe
        (
            "Unknown capability section 'not-a-capability'. Use get_settings to see valid sections for this app."
        );

        await AssertNoEventRecordedAsync();
    }

    // The REST path (RejectUnknownSection false) SKIPS an unknown section and succeeds.
    [Fact]
    public async Task UnknownSection_RejectFalse_SkipsAndSucceeds()
    {
        await SeedStaticSiteAsync("site-rest");

        var (operation, _) = await CreateOperationAsync();

        var command = new UpdateSettingsCommand
        (
            "site-rest",
            ChangesFor("not-a-capability", ("foo", "bar")),
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Slug.ShouldBe("site-rest");

        // No override was saved (the only section was unknown and skipped), but the event still fires
        // (matching pre-migration REST -- the event records the changed-capabilities, here empty).
        await AssertEventRecordedAsync(ActivityEventTypes.AppSettingsUpdated, "site-rest");
    }

    // --- Helpers ---

    private static JsonObject ChangesFor(string section, params (string Key, JsonNode? Value)[] fields)
    {
        var sectionObject = new JsonObject();

        foreach (var (key, value) in fields)
        {
            sectionObject[key] = value;
        }

        return new JsonObject { [section] = sectionObject };
    }

    private async Task<(UpdateSettingsOperation Operation, ProxyManager Proxy)> CreateOperationAsync()
    {
        var (typeStore, capabilityStore) = await CreateStoresAsync();
        var supervisor = CreateSupervisor(typeStore, capabilityStore);
        var proxy = CreateProxyManager(supervisor, typeStore, capabilityStore);
        var probeService = new ProbeService(_appStore, capabilityStore, TimeProvider.System, NullLogger<ProbeService>.Instance);
        var writer = new RuntimeConfigFileWriter
        (
            capabilityStore,
            new AppDataPathResolver(Path.GetTempPath()),
            NullLogger<RuntimeConfigFileWriter>.Instance
        );

        var operation = new UpdateSettingsOperation
        (
            _appStore,
            typeStore,
            probeService,
            proxy,
            writer,
            new ExternalTargetSettings { AllowPublicHosts = false },
            _currentUser,
            _activityEventStore
        );

        return (operation, proxy);
    }

    private async Task<App> SeedStaticSiteAsync(string slug)
    {
        await using var context = await _dbFactory.CreateDbContextAsync();

        var app = new App
        {
            Slug = slug,
            DisplayName = "Seed",
            AppTypeSlug = "static-site"
        };

        context.Apps.Add(app);
        await context.SaveChangesAsync();

        return app;
    }

    private async Task<(TypeStore TypeStore, CapabilityStore CapabilityStore)> CreateStoresAsync()
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h"
        };

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-406pr5-notexist") },
            settings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await typeStore.LoadAsync();

        var capabilityStore = new CapabilityStore(typeStore, _appStore, NullLogger<CapabilityStore>.Instance);

        return (typeStore, capabilityStore);
    }

    private ProcessSupervisor CreateSupervisor(TypeStore typeStore, CapabilityStore capabilityStore)
    {
        var bundleDirectory = new HostedAppBundleDirectory(Path.GetTempPath(), NullLogger<HostedAppBundleDirectory>.Instance);

        return new ProcessSupervisor
        (
            new UnusedRunner(),
            new NullContainment(),
            _appStore,
            capabilityStore,
            typeStore,
            new EventBus<ProcessStateChangedEvent>(),
            [],
            [],
            [],
            bundleDirectory,
            new PortAllocator(),
            _activityEventStore,
            NullLogger<ProcessSupervisor>.Instance
        );
    }

    private ProxyManager CreateProxyManager
    (
        ProcessSupervisor supervisor,
        TypeStore typeStore,
        CapabilityStore capabilityStore
    )
    {
        var settings = new ProxySettings
        {
            BaseDomain = "collab.internal",
            BinaryPath = null,
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        return new ProxyManager
        (
            new NoOpCaddyClient(),
            _appStore,
            capabilityStore,
            typeStore,
            supervisor,
            new EventBus<ProcessStateChangedEvent>(),
            settings,
            new HostingSettings { ListenAddress = "localhost", ListenPort = 58500 },
            new PortalSettings { Subdomain = "collabhost" },
            _activityEventStore,
            new RuntimeConfigFileWriter
            (
                capabilityStore,
                new AppDataPathResolver(Path.GetTempPath()),
                NullLogger<RuntimeConfigFileWriter>.Instance
            ),
            new AppDataPathResolver(Path.GetTempPath()),
            TimeProvider.System,
            NullLogger<ProxyManager>.Instance
        );
    }

    private async Task AssertNoEventRecordedAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        (await db.ActivityEvents.CountAsync()).ShouldBe(0);
    }

    private async Task AssertEventRecordedAsync(string eventType, string slug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var recorded = await db.ActivityEvents.SingleAsync();

        recorded.EventType.ShouldBe(eventType);
        recorded.AppSlug.ShouldBe(slug);
        recorded.ActorId.ShouldBe(_actor.Id.ToString(null, CultureInfo.InvariantCulture));
        recorded.ActorName.ShouldBe(_actor.Name);
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class TestDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
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

file sealed class NoOpCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<LoadConfigResult> LoadConfigAsync(System.Text.Json.Nodes.JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<System.Text.Json.Nodes.JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<System.Text.Json.Nodes.JsonObject?>(null);
}

file sealed class UnusedRunner : IManagedProcessRunner
{
    public IProcessHandle Start(ProcessStartConfiguration configuration) =>
        throw new NotSupportedException("No process should be started in these tests.");

    public Task<ProcessRunResult> RunToCompletionAsync
    (
        ProcessStartConfiguration configuration,
        TimeSpan timeout,
        CancellationToken ct = default
    ) =>
        throw new NotSupportedException("No process should be run in these tests.");
}
