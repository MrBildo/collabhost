using System.Globalization;

using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Card #166 -- SIGHUP fallback reload for user types. The contract has three layers:
//
//   1. PosixSignalRegistration setup (Linux only): StartAsync registers a SIGHUP handler
//      without throwing; StopAsync disposes it.
//   2. Reload trigger logic (cross-platform): TriggerReload (extracted from OnSighup) drives
//      TypeStore.ReloadAsync via fire-and-forget, surfaces the "SIGHUP" trigger source in
//      the reload log, and survives reload exceptions without crashing.
//   3. End-to-end signal delivery: kill -HUP <pid> triggers the registered handler in the
//      running collabhost binary. NOT exercised in unit tests -- xunit's test host owns the
//      process and SIGHUP delivery to the test runner is unsafe (the .NET runtime's default
//      action terminates the host even when our handler sets PosixSignalContext.Cancel = true,
//      because the test runtime may not have the same generic-host signal hooks the
//      collabhost binary does). Layer 3 is exercised at dispatch via the build-and-publish
//      smoke run on Linux.
//
// Layers 1 and 2 are what we own; layer 3 is .NET runtime + libc.
public class SighupReloadServiceTests : IDisposable
{
    private readonly string _userTypesDirectory;

    private static readonly ProxySettings _defaultProxySettings = new()
    {
        BaseDomain = "collab.internal",
        BinaryPath = "caddy",
        ListenAddress = ":443",
        CertLifetime = "168h"
    };

    private static readonly string _validUserTypeJson = """
        {
          "slug": "sighup-test-app",
          "displayName": "SIGHUP Test Application",
          "description": "Validates SIGHUP-triggered reload",
          "bindings": {
            "process": {
              "discoveryStrategy": "Manual",
              "shutdownTimeoutSeconds": 10
            }
          }
        }
        """;

    public SighupReloadServiceTests()
    {
        _userTypesDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-sighup-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );

        Directory.CreateDirectory(_userTypesDirectory);
    }

    private TypeStore CreateTypeStore() =>
        new
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = _userTypesDirectory },
            _defaultProxySettings,
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

    [SkippableFact]
    public async Task StartAsync_OnNonLinux_NoOpReturnsCleanly()
    {
        // On Windows / macOS the service must start and stop cleanly without registering
        // anything. The hosted service is wired into DI unconditionally; the platform gate
        // is internal so the DI graph stays identical across platforms.
        Skip.If(OperatingSystem.IsLinux(), "Non-Linux only");

        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // The lifetime must not be touched on non-Linux -- the no-op shape is silent.
        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse();
    }

    [SkippableFact]
    public async Task StartAsync_OnLinux_RegistersWithoutThrowing()
    {
        // Layer 1 of the contract: registering the PosixSignal.SIGHUP handler must succeed
        // on Linux. PosixSignalRegistration.Create can throw PlatformNotSupportedException
        // on platforms where the signal is unsupported -- this test is the proof Linux is
        // a supported host.
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        // No exception escaping StartAsync IS the assertion; followed by a clean stop to verify
        // the registration disposes without complaint.
        await Should.NotThrowAsync(() => service.StartAsync(CancellationToken.None));
        await Should.NotThrowAsync(() => service.StopAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TriggerReload_PicksUpNewUserTypeFile()
    {
        // Layer 2 of the contract: the handler's reload-triggering code path drives
        // TypeStore.ReloadAsync and the snapshot picks up the new file. The test reaches into
        // the internal TriggerReload() method (extracted from OnSighup so it can be exercised
        // without delivering a real signal). Cross-platform -- the reload mechanics are
        // identical regardless of how the trigger arrived.
        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        typeStore.ListTypes().Count.ShouldBe(7);

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        // Drop a user-type file. The FSW is NOT started -- the only path from "file on disk"
        // to "snapshot updated" in this test is through TriggerReload.
        await File.WriteAllTextAsync
        (
            Path.Combine(_userTypesDirectory, "sighup-test-app.json"),
            _validUserTypeJson,
            lifetime.ApplicationStopping
        );

        // Act: invoke the reload-trigger path the SIGHUP handler runs.
        service.TriggerReload();

        // Assert: the snapshot picks up the new user type within a reasonable window. The
        // trigger fires-and-forgets a Task.Run, so we have to poll for the snapshot update.
        await WaitForConditionAsync
        (
            () => typeStore.ListTypes().Count == 8,
            lifetime.ApplicationStopping,
            timeoutMilliseconds: 5000
        );

        typeStore.ListTypes().Count.ShouldBe(8);

        var customType = typeStore.GetBySlug("sighup-test-app");
        customType.ShouldNotBeNull();
        customType.IsBuiltIn.ShouldBeFalse();
        customType.DisplayName.ShouldBe("SIGHUP Test Application");
    }

    [Fact]
    public async Task TriggerReload_DoesNotCancelTheLifetime()
    {
        // The reload-trigger path must not surface as a cancellation on the host's lifetime
        // token. The host stays running; the reload is a snapshot-swap concern.
        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        service.TriggerReload();

        // Give the fire-and-forget task time to run.
        using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await Task.Delay(500, delayCts.Token);

        lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse
        (
            "TriggerReload must not cancel the application lifetime"
        );
    }

    [Fact]
    public async Task TriggerReload_LogsTheSighupTriggerSource()
    {
        // The trigger-source string surfaces in the reload log line so operators can tell
        // which path fired the reload (FSW vs SIGHUP vs anything we add later). This pins
        // the "SIGHUP" string into the contract.
        var capturingLogger = new CapturingLogger<TypeStore>();

        var typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = _userTypesDirectory },
            _defaultProxySettings,
            new StubHostEnvironment(),
            capturingLogger
        );

        await typeStore.LoadAsync();

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        service.TriggerReload();

        await WaitForConditionAsync
        (
            () => capturingLogger.Messages.Any(m => m.Contains("triggered by SIGHUP", StringComparison.Ordinal)),
            lifetime.ApplicationStopping,
            timeoutMilliseconds: 5000
        );

        capturingLogger.Messages.ShouldContain
        (
            m => m.Contains("triggered by SIGHUP", StringComparison.Ordinal),
            "Reload log must name SIGHUP as the trigger source"
        );
    }

    private static async Task WaitForConditionAsync
    (
        Func<bool> condition,
        CancellationToken cancellationToken,
        int timeoutMilliseconds = 5000,
        int pollIntervalMilliseconds = 100
    )
    {
        var elapsed = 0;

        while (!condition() && elapsed < timeoutMilliseconds)
        {
            await Task.Delay(pollIntervalMilliseconds, cancellationToken);
            elapsed += pollIntervalMilliseconds;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_userTypesDirectory))
        {
            try
            {
                Directory.Delete(_userTypesDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    private sealed class StubApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
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

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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
            ArgumentNullException.ThrowIfNull(formatter);

            var message = formatter(state, exception);

            lock (_gate)
            {
                _messages.Add(message);
            }
        }
    }
}
