using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Registry;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Capabilities;

public class CapabilityStoreTests : IAsyncLifetime, IDisposable
{
    private TypeStore _typeStore = null!;
    private CapabilityStore _capabilityStore = null!;
    private SqliteConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-notexist") },
            NullLogger<TypeStore>.Instance
        );

        await _typeStore.LoadAsync();

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbFactory = new InMemoryDbContextFactory(_connection);

        // Ensure the schema exists by running the migration
        await using var context = await dbFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var appStore = new AppStore
        (
            dbFactory,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AppStore>.Instance
        );

        _capabilityStore = new CapabilityStore
        (
            _typeStore,
            appStore,
            NullLogger<CapabilityStore>.Instance
        );
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ResolveAsync_DotNetApp_ProcessCapability_ReturnsProcessConfiguration()
    {
        var result = await _capabilityStore.ResolveAsync<ProcessConfiguration>
        (
            "process",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.DiscoveryStrategy.ShouldBe(DiscoveryStrategy.DotNetRuntimeConfiguration);
    }

    [Fact]
    public async Task ResolveAsync_DotNetApp_RestartCapability_ReturnsRestartConfiguration()
    {
        var result = await _capabilityStore.ResolveAsync<RestartConfiguration>
        (
            "restart",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.Policy.ShouldBe(RestartPolicy.OnCrash);
    }

    [Fact]
    public async Task ResolveAsync_DotNetApp_RoutingCapability_ReturnsReverseProxy()
    {
        var result = await _capabilityStore.ResolveAsync<RoutingConfiguration>
        (
            "routing",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.ServeMode.ShouldBe(ServeMode.ReverseProxy);
    }

    [Fact]
    public async Task ResolveAsync_StaticSite_RoutingCapability_ReturnsFileServer()
    {
        var result = await _capabilityStore.ResolveAsync<RoutingConfiguration>
        (
            "routing",
            "static-site",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.ServeMode.ShouldBe(ServeMode.FileServer);
    }

    [Fact]
    public async Task ResolveAsync_StaticSite_ProcessCapability_ReturnsNull()
    {
        var result = await _capabilityStore.ResolveAsync<ProcessConfiguration>
        (
            "process",
            "static-site",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnknownAppType_ReturnsNull()
    {
        var result = await _capabilityStore.ResolveAsync<ProcessConfiguration>
        (
            "process",
            "nonexistent-type",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnknownCapability_ReturnsNull()
    {
        var result = await _capabilityStore.ResolveAsync<ProcessConfiguration>
        (
            "nonexistent-capability",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldBeNull();
    }

    [Fact]
    public async Task ResolveJsonAsync_DotNetApp_ProcessCapability_ReturnsJson()
    {
        var result = await _capabilityStore.ResolveJsonAsync
        (
            "process",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.ShouldContain("DotNetRuntimeConfiguration");
    }

    [Fact]
    public async Task ResolveAllJsonAsync_DotNetApp_ReturnsAllBindings()
    {
        var result = await _capabilityStore.ResolveAllJsonAsync
        (
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.Count.ShouldBe(8);
        result.ShouldContainKey("process");
        result.ShouldContainKey("routing");
        result.ShouldContainKey("restart");
        result.ShouldContainKey("artifact");
        result.ShouldContainKey("auto-start");
        result.ShouldContainKey("port-injection");
        result.ShouldContainKey("environment-defaults");
        result.ShouldContainKey("health-check");
    }

    [Fact]
    public async Task ResolveAllJsonAsync_UnknownAppType_ReturnsEmptyDictionary()
    {
        var result = await _capabilityStore.ResolveAllJsonAsync
        (
            "nonexistent-type",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact]
    public async Task ResolveAsync_DotNetApp_EnvironmentDefaults_ReturnsExpectedVariables()
    {
        var result = await _capabilityStore.ResolveAsync<EnvironmentConfiguration>
        (
            "environment-defaults",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
        result.Variables.ShouldNotBeNull();
        result.Variables.ShouldContainKey("ASPNETCORE_ENVIRONMENT");
        result.Variables.ShouldContainKey("DOTNET_ENVIRONMENT");
    }

    [Fact]
    public async Task ResolveAsync_DotNetApp_AutoStart_ReturnsAutoStartConfiguration()
    {
        var result = await _capabilityStore.ResolveAsync<AutoStartConfiguration>
        (
            "auto-start",
            "dotnet-app",
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("dotnet-app", 8)]
    [InlineData("nodejs-app", 8)]
    [InlineData("static-site", 2)]
    [InlineData("system-service", 4)]
    [InlineData("executable", 6)]
    public async Task ResolveAllJsonAsync_AllTypes_ReturnsCorrectBindingCount
    (
        string appTypeSlug,
        int expectedCount
    )
    {
        var result = await _capabilityStore.ResolveAllJsonAsync
        (
            appTypeSlug,
            Ulid.NewUlid(),
            CancellationToken.None
        );

        result.Count.ShouldBe(expectedCount);
    }
}

// In-memory SQLite factory for isolated testing -- each test gets a fresh context
// over the shared connection (which preserves the schema for the test lifetime).
file sealed class InMemoryDbContextFactory
(
    SqliteConnection connection
) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return new AppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return Task.FromResult(new AppDbContext(options));
    }
}
