namespace Collabhost.Api.Probes;

// Warms the probe cache on boot. PRB-01: the initial full-tree scan of every app's artifact
// directory must NOT block startup -- IHostedService.StartAsync is awaited before
// ApplicationStarted fires, so awaiting the scan here delayed the API accepting requests by
// however long the slowest artifact dir took to walk (large or network-mounted artifacts could
// stall boot for seconds). The scan is now kicked from the ApplicationStarted callback as a
// detached task: the host comes up immediately, and the cache reports NeverProbed until the
// scan completes (the UI degrades gracefully on that state).
public class ProbeStartupService
(
    ProbeService probeService,
    IHostApplicationLifetime lifetime,
    ILogger<ProbeStartupService> logger
)
    : IHostedService
{
    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    private readonly IHostApplicationLifetime _lifetime = lifetime
        ?? throw new ArgumentNullException(nameof(lifetime));

    private readonly ILogger<ProbeStartupService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Defer the scan to ApplicationStarted so it never sits on the startup critical path.
        _lifetime.ApplicationStarted.Register(() =>
        {
#pragma warning disable VSTHRD110, MA0134, CS4014, CA2016, MA0040
            RunInitialScanAsync(_lifetime.ApplicationStopping);
#pragma warning restore VSTHRD110, MA0134, CS4014, CA2016, MA0040
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RunInitialScanAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running initial probe scan for all registered apps");

        try
        {
            await _probeService.RunProbesForAllAppsAsync(cancellationToken);

            _logger.LogInformation("Initial probe scan complete");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host is shutting down before the scan finished -- expected, not an error.
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to complete initial probe scan");
        }
    }
}
