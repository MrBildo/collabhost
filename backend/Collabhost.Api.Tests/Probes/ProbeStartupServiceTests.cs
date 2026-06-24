using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// PRB-01: the initial probe scan must NOT block startup. IHostedService.StartAsync is awaited
// before ApplicationStarted fires, so awaiting the scan in StartAsync delayed the API accepting
// requests by the slowest artifact-dir walk. The fix defers the scan to an ApplicationStarted
// callback. These tests prove the deferral via the scan's own opening log line ("Running initial
// probe scan for all registered apps"): it must be absent after StartAsync returns, and present
// only after ApplicationStarted fires.
#pragma warning disable CA1001 // SqliteConnection disposed via IDisposable below
public sealed class ProbeStartupServiceTests : IDisposable
#pragma warning restore CA1001
{
    private readonly SqliteConnection _connection;
    private readonly ProbeService _probeService;

    public ProbeStartupServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbFactory = new ProbeStartupDbContextFactory(_connection);

        using (var db = dbFactory.CreateDbContext())
        {
            db.Database.EnsureCreated();
        }

        var appStore = new AppStore(dbFactory, new MemoryCache(new MemoryCacheOptions()), NullLogger<AppStore>.Instance);

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-prb01-notexist") },
            new ProxySettings { BaseDomain = "collab.internal", BinaryPath = null, ListenAddress = ":443", CertLifetime = "168h" },
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        var capabilityStore = new CapabilityStore(typeStore, appStore, NullLogger<CapabilityStore>.Instance);

        _probeService = new ProbeService(appStore, capabilityStore, TimeProvider.System, NullLogger<ProbeService>.Instance);
    }

    [Fact]
    public async Task StartAsync_DoesNotRunScan_UntilApplicationStarted()
    {
        var logger = new CapturingLogger<ProbeStartupService>();
        using var lifetime = new FakeHostApplicationLifetime();

        var service = new ProbeStartupService(_probeService, lifetime, logger);

        await service.StartAsync(CancellationToken.None);

        // PRB-01: StartAsync returned without running (or awaiting) the scan -- the opening log line
        // is absent. Pre-fix, StartAsync awaited RunProbesForAllAppsAsync inline, so this line would
        // already be present here.
        logger.Messages.ShouldNotContain(m => m.Contains("Running initial probe scan", StringComparison.Ordinal));

        // Firing ApplicationStarted runs the deferred scan; the opening log line now appears.
        lifetime.TriggerApplicationStarted();

        await WaitUntilAsync
        (
            () => logger.Messages.Any(m => m.Contains("Running initial probe scan", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5)
        );

        logger.Messages.ShouldContain(m => m.Contains("Running initial probe scan", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    public void Dispose() => _connection.Dispose();
}

file sealed class ProbeStartupDbContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
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

file sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;

    public void TriggerApplicationStarted() => _started.Cancel();

    public void StopApplication() => _stopping.Cancel();

    public void Dispose()
    {
        _started.Dispose();
        _stopping.Dispose();
        _stopped.Dispose();
    }
}

file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>
    (
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        lock (_gate)
        {
            _messages.Add(formatter(state, exception));
        }
    }
}

// Non-generic so the shared no-op scope instance isn't a static field on a generic type (S2743).
file sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();

    public void Dispose()
    {
    }
}
