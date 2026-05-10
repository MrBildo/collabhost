using System.Runtime.InteropServices;

using Collabhost.Api.Data.AppTypes;

namespace Collabhost.Api.Platform;

// Linux-only fallback path for hot-reloading user types. The FileSystemWatcher started by
// TypeStore.StartWatching is the primary mechanism, but FSW is unreliable on network
// filesystems (NFS, SMB) and on some container overlay mounts (Docker / Podman). SIGHUP
// gives the operator an explicit "reload now" lever -- `kill -HUP <pid>` and TypeStore
// re-reads the user-types directory.
//
// The signal handler cannot be registered on Windows: PosixSignalRegistration.Create
// throws PlatformNotSupportedException for SIGHUP on Windows because the underlying
// signal does not exist there. The hosted service is registered unconditionally; the
// gate is inside StartAsync so the service still starts and stops cleanly on Windows
// (no-op shape), which keeps the DI graph identical across platforms.
public class SighupReloadService
(
    TypeStore typeStore,
    IHostApplicationLifetime applicationLifetime,
    ILogger<SighupReloadService> logger
) : IHostedService, IDisposable
{
    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly IHostApplicationLifetime _applicationLifetime = applicationLifetime
        ?? throw new ArgumentNullException(nameof(applicationLifetime));

    private readonly ILogger<SighupReloadService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private PosixSignalRegistration? _registration;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            // Windows / macOS: the bench-clear scope ships SIGHUP only for Linux. macOS supports
            // POSIX signals, but the FSW reliability gap that motivates the card is a Linux
            // network-filesystem / overlay-mount concern. Revisit if a macOS user-type reload
            // gap surfaces.
            _logger.LogDebug("SIGHUP reload handler not registered: not running on Linux");

            return Task.CompletedTask;
        }

        try
        {
            _registration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, OnSighup);

            _logger.LogInformation
            (
                "SIGHUP reload handler registered (kill -HUP {Pid} to force user-type reload)",
                Environment.ProcessId
            );
        }
        catch (PlatformNotSupportedException ex)
        {
            // Defensive belt-and-suspenders: OperatingSystem.IsLinux() should have already gated
            // this. If we land here on a Linux-reporting platform, surface the message and
            // continue -- FSW remains the primary reload path.
            _logger.LogWarning
            (
                ex,
                "SIGHUP reload handler not registered (PosixSignal.SIGHUP unsupported on this runtime)"
            );
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _registration?.Dispose();
        _registration = null;

        return Task.CompletedTask;
    }

    private void OnSighup(PosixSignalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Suppress the runtime's default SIGHUP handling (controlled-shutdown). We want SIGHUP
        // to mean "reload" while Collabhost is running, not "shut down."
        context.Cancel = true;

        // Capture the lifetime cancellation outside the async continuation so the handler does
        // not touch instance state after Dispose. The signal callback runs on a thread-pool
        // thread; fire-and-forget the reload so the signal-delivery thread is never blocked.
        var stoppingToken = _applicationLifetime.ApplicationStopping;

        _ = Task.Run
        (
            async () =>
            {
                try
                {
                    await _typeStore.ReloadAsync("SIGHUP", stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected during shutdown -- the host is stopping while a reload was in flight.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SIGHUP-triggered TypeStore reload failed");
                }
            },
            stoppingToken
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _registration?.Dispose();
            _registration = null;
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
