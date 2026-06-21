namespace Collabhost.Api.ActivityLog;

// Hosted service that runs the ActivityEvents retention sweep on a fixed cadence (SVC-01). Mirrors
// ProbePeriodicService / HealthCheckExecutorService in shape: BackgroundService + PeriodicTimer +
// an internal TickAsync the test drives deterministically. The sweep runs OFF the insert hot path
// -- a per-insert cap-and-prune would tax the exact 5-b-tree write the finding flags as costly --
// so growth is bounded on a timer instead.
public class ActivityEventRetentionService
(
    ActivityEventStore store,
    ActivityEventRetentionSettings retention,
    TimeProvider timeProvider,
    ILogger<ActivityEventRetentionService> logger
) : BackgroundService
{
    private readonly ActivityEventStore _store = store
        ?? throw new ArgumentNullException(nameof(store));

    private readonly ActivityEventRetentionSettings _retention = retention
        ?? throw new ArgumentNullException(nameof(retention));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<ActivityEventRetentionService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private TimeSpan SweepInterval => TimeSpan.FromMinutes(_retention.SweepIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation
        (
            "Activity-event retention sweep started -- cadence {IntervalMinutes}m, keep {MaxCount} rows / {MaxAgeDays} days",
            _retention.SweepIntervalMinutes,
            _retention.MaxCount,
            _retention.MaxAgeDays
        );

        // PeriodicTimer drifts when a tick takes longer than the period; that is the right behavior
        // for a retention sweep (never queue up missed ticks -- the next tick captures current state).
        using var timer = new PeriodicTimer(SweepInterval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(exception, "Activity-event retention sweep tick failed");
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

    // Internal so the integration test can drive a single sweep deterministically.
    internal Task TickAsync(CancellationToken ct) => _store.PruneAsync(_retention, ct);
}
