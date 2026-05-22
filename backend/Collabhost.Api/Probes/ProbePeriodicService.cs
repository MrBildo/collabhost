namespace Collabhost.Api.Probes;

// Hosted service that re-scans every registered app on a fixed cadence so the
// probe cache stays warm without depending on operator-triggered events (start,
// settings save, etc.) -- Card #337. Mirrors HealthCheckExecutorService and
// ProcessResourceSamplerService in shape: BackgroundService + PeriodicTimer +
// tick body that consults the live app set.
//
// Cadence is intentionally half the ProbeService.FreshnessWindow so under
// normal operation cache entries cycle Fresh -> Fresh; a Stale read is the
// signal that the periodic loop has stopped advancing (process hang, repeated
// extraction failures, etc.).
public class ProbePeriodicService
(
    ProbeService probeService,
    TimeProvider timeProvider,
    ILogger<ProbePeriodicService> logger
) : BackgroundService
{
    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<ProbePeriodicService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    // Internal so tests can override. 15-minute production cadence sits at half
    // the default 30-minute freshness window so a single missed tick does not
    // surface as Stale.
    internal TimeSpan ScanInterval { get; init; } = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation
        (
            "Probe periodic re-scan started -- cadence {IntervalMinutes}m",
            ScanInterval.TotalMinutes
        );

        // PeriodicTimer drifts when a tick takes longer than the period; this is
        // the right behavior for a refresher (we never want to queue up missed
        // ticks -- the next tick captures current state).
        using var timer = new PeriodicTimer(ScanInterval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(exception, "Probe periodic re-scan tick failed");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Internal so the integration test can drive a single tick deterministically.
    internal Task TickAsync(CancellationToken ct) =>
        _probeService.RunProbesForAllAppsAsync(ct);
}
