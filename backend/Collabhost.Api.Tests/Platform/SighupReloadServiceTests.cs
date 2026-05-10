using System.Globalization;
using System.Runtime.Versioning;

using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Linux-only integration coverage for the SIGHUP fallback reload (card #166). The contract:
//
//   1. On Linux, registering the hosted service installs a PosixSignal.SIGHUP handler.
//   2. Sending SIGHUP to our own PID triggers TypeStore.ReloadAsync.
//   3. The handler suppresses the runtime's default SIGHUP shutdown behavior so the host
//      continues running.
//
// Tests skip on non-Linux: PosixSignalRegistration.Create throws PlatformNotSupportedException
// for SIGHUP on Windows, and the FSW reliability gap that motivates the card is Linux-specific.
// macOS supports POSIX signals at the runtime level, but the bench-clear scope limits the
// feature to Linux; revisit if a macOS user-type reload gap surfaces.
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
    public async Task StartAsync_OnNonLinux_ReturnsImmediatelyWithoutThrowing()
    {
        // Inverse of the Linux-gated tests below: on Windows / macOS the service must start
        // and stop cleanly without registering anything. The hosted service is wired into DI
        // unconditionally; the platform gate is internal.
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
    [SupportedOSPlatform("linux")]
    public async Task SighupSent_OnLinux_TriggersTypeStoreReload()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        // Arrange: load the TypeStore with built-ins only, then register the SIGHUP handler.
        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        typeStore.ListTypes().Count.ShouldBe(5);

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        await service.StartAsync(CancellationToken.None);

        try
        {
            // Drop a user-type file -- we deliberately don't start the FSW here. The only path
            // from "file on disk" to "snapshot updated" is through the SIGHUP-triggered reload.
            await File.WriteAllTextAsync
            (
                Path.Combine(_userTypesDirectory, "sighup-test-app.json"),
                _validUserTypeJson,
                lifetime.ApplicationStopping
            );

            // Act: send SIGHUP to our own process. The handler runs on a thread-pool thread
            // and fire-and-forgets the reload, so we have to poll for the snapshot update.
            var killResult = LinuxNativeMethods.Kill(Environment.ProcessId, LinuxNativeMethods.SIGHUP);
            killResult.ShouldBe(0, "kill(pid, SIGHUP) should succeed against our own PID");

            // Assert: the snapshot picks up the new user type within a reasonable window.
            await WaitForConditionAsync
            (
                () => typeStore.ListTypes().Count == 6,
                lifetime.ApplicationStopping,
                timeoutMilliseconds: 5000
            );

            typeStore.ListTypes().Count.ShouldBe(6);

            var customType = typeStore.GetBySlug("sighup-test-app");
            customType.ShouldNotBeNull();
            customType.IsBuiltIn.ShouldBeFalse();
            customType.DisplayName.ShouldBe("SIGHUP Test Application");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [SkippableFact]
    [SupportedOSPlatform("linux")]
    public async Task SighupSent_OnLinux_DoesNotShutDownTheHost()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        // The runtime's default SIGHUP behavior is cooperative shutdown via IHostApplicationLifetime.
        // Our handler sets PosixSignalContext.Cancel = true to inhibit that; this test pins the
        // contract -- after a SIGHUP, the lifetime is still in the running state.
        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        await service.StartAsync(CancellationToken.None);

        try
        {
            var killResult = LinuxNativeMethods.Kill(Environment.ProcessId, LinuxNativeMethods.SIGHUP);
            killResult.ShouldBe(0);

            // Give the handler time to run and any cancellation to propagate (it shouldn't).
            // The CTS used here is independent of lifetime to allow this delay to run to completion.
            using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await Task.Delay(500, delayCts.Token);

            lifetime.ApplicationStopping.IsCancellationRequested.ShouldBeFalse
            (
                "SIGHUP must not cancel the application lifetime"
            );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [SkippableFact]
    [SupportedOSPlatform("linux")]
    public async Task StopAsync_OnLinux_DisposesTheRegistration()
    {
        Skip.IfNot(OperatingSystem.IsLinux(), "Linux only");

        // Once the service stops, subsequent SIGHUPs must not trigger a reload. The handler
        // registration is owned by PosixSignalRegistration; disposing it removes the callback.
        var typeStore = CreateTypeStore();
        await typeStore.LoadAsync();

        typeStore.ListTypes().Count.ShouldBe(5);

        using var lifetime = new StubApplicationLifetime();
        using var service = new SighupReloadService(typeStore, lifetime, NullLogger<SighupReloadService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        // After StopAsync, the registration is disposed. Drop a file and signal -- the snapshot
        // must NOT update.
        await File.WriteAllTextAsync
        (
            Path.Combine(_userTypesDirectory, "sighup-test-app.json"),
            _validUserTypeJson,
            lifetime.ApplicationStopping
        );

        var killResult = LinuxNativeMethods.Kill(Environment.ProcessId, LinuxNativeMethods.SIGHUP);
        killResult.ShouldBe(0);

        // Give any straggler handlers time to run (there shouldn't be any from us).
        using var delayCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Task.Delay(1000, delayCts.Token);

        // Snapshot is still 5 -- our handler did not fire.
        typeStore.ListTypes().Count.ShouldBe(5);
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
}
