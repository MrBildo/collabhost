namespace Collabhost.Api.Supervisor.Resources;

// Hosted service that drives the resource sampler on a fixed cadence. AppEndpoints
// reads from the cache only -- the per-request cost on the detail page is a single
// dictionary lookup. The 5-second cadence is a homelab-platform compromise: a memory
// or CPU spike shows up within 5s on the App Detail page, and the sampler costs ~one
// cheap /proc read per running process per tick (Linux) or one Process.Refresh call
// per tick (Windows). For a host running 5 apps, that is well under 1ms per tick.
public class ProcessResourceSamplerService
(
    IProcessResourceSampler sampler,
    ProcessSupervisor supervisor,
    ProcessResourceCache cache,
    TimeProvider timeProvider,
    ILogger<ProcessResourceSamplerService> logger
) : BackgroundService
{
    private readonly IProcessResourceSampler _sampler = sampler
        ?? throw new ArgumentNullException(nameof(sampler));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly ProcessResourceCache _cache = cache
        ?? throw new ArgumentNullException(nameof(cache));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<ProcessResourceSamplerService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    // Internal so tests can override. 5s is the production cadence.
    internal TimeSpan SampleInterval { get; init; } = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation
        (
            "Process resource sampler started -- cadence {IntervalSeconds}s",
            SampleInterval.TotalSeconds
        );

        // PeriodicTimer drifts when ticks take longer than the period; for a sampler
        // this is the correct behavior (we never want to queue up missed ticks).
        using var timer = new PeriodicTimer(SampleInterval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                SampleAll();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(exception, "Resource sampler tick failed");
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

    private void SampleAll()
    {
        foreach (var process in _supervisor.GetProcesses())
        {
            if (!process.IsRunning || process.Pid is not int pid)
            {
                _cache.Remove(process.AppId);

                // SUP-15: forget the per-PID CPU baseline for a process that is no longer running
                // but still carries a PID (Stopping/Backoff retain it mid-shutdown/retry). The
                // null-snapshot branch below already forgets; this branch did not, so a baseline
                // could survive past the process and poison a later PID-reuse's first CPU% delta.
                if (process.Pid is int stoppedPid)
                {
                    _sampler.Forget(stoppedPid);
                }

                continue;
            }

            var snapshot = _sampler.Sample(pid);

            if (snapshot is not null)
            {
                _cache.Set(process.AppId, snapshot);
            }
            else
            {
                _cache.Remove(process.AppId);
                _sampler.Forget(pid);
            }
        }
    }
}
