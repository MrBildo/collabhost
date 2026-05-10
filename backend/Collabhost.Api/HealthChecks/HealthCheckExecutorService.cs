using System.Collections.Concurrent;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.HealthChecks;

// Hosted service that polls each app's configured /health endpoint at the configured
// intervalSeconds. A single tick (every TickInterval) walks every running, health-
// check-enabled app and decides whether the app is due for a probe. Probes run in
// parallel via Task.WhenAll so a single slow endpoint does not block the others.
//
// Persistence: results are kept in-memory only. On Collabhost restart every app is
// "unknown" again until the first tick after start-up. That matches the contract
// the frontend expects -- pre-first-probe -> null -> "--".
public class HealthCheckExecutorService
(
    AppStore appStore,
    TypeStore typeStore,
    CapabilityStore capabilityStore,
    ProcessSupervisor supervisor,
    HealthCheckProbe probe,
    TimeProvider timeProvider,
    ILogger<HealthCheckExecutorService> logger
) : BackgroundService, IHealthCheckExecutor
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly ProcessSupervisor _supervisor = supervisor
        ?? throw new ArgumentNullException(nameof(supervisor));

    private readonly HealthCheckProbe _probe = probe
        ?? throw new ArgumentNullException(nameof(probe));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<HealthCheckExecutorService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<Ulid, HealthCheckResult> _latest = new();
    private readonly ConcurrentDictionary<Ulid, DateTime> _lastProbedAt = new();

    // The tick is a coarse "are any apps due?" loop; each app's intervalSeconds is
    // honored independently. Internal-with-init so tests can override the cadence.
    internal TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    public HealthCheckResult? GetLatest(Ulid appId) =>
        _latest.TryGetValue(appId, out var result) ? result : null;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation
        (
            "Health check executor started -- tick interval {TickSeconds}s",
            TickInterval.TotalSeconds
        );

        using var timer = new PeriodicTimer(TickInterval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _logger.LogWarning(exception, "Health check executor tick failed");
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

    // Internal so the integration test fixture can drive a single tick deterministically.
    internal async Task TickAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var probeTasks = new List<Task>();

        foreach (var app in apps)
        {
            // Skip apps without the health-check capability bound. This is the cheapest
            // filter and avoids a CapabilityStore.ResolveAsync round-trip for every app
            // that does not have a health endpoint configured.
            if (!_typeStore.HasBinding(app.AppTypeSlug, "health-check"))
            {
                _latest.TryRemove(app.Id, out _);
                _lastProbedAt.TryRemove(app.Id, out _);
                continue;
            }

            var process = _supervisor.GetProcess(app.Id);

            if (process is null || !process.IsRunning || process.Port is null)
            {
                // Not running -> clear stale "healthy" / "unhealthy" results.
                // The reader returns null in this state and the UI renders "--".
                _latest.TryRemove(app.Id, out _);
                _lastProbedAt.TryRemove(app.Id, out _);
                continue;
            }

            var configuration = await _capabilityStore.ResolveAsync<HealthCheckConfiguration>
            (
                "health-check", app, ct
            );

            if (configuration is null)
            {
                continue;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(1, configuration.IntervalSeconds));

            if (_lastProbedAt.TryGetValue(app.Id, out var lastProbedAt) && now - lastProbedAt < interval)
            {
                continue;
            }

            _lastProbedAt[app.Id] = now;

            var port = process.Port.Value;
            var appId = app.Id;
            var slug = app.Slug;

            probeTasks.Add(Task.Run(async () =>
            {
                var result = await _probe.ProbeAsync(slug, port, configuration, ct);
                _latest[appId] = result;
            }, ct));
        }

        if (probeTasks.Count > 0)
        {
            // Each probe task handles its own exceptions inside HealthCheckProbe.ProbeAsync.
            // WhenAll only waits for the batch to complete.
            await Task.WhenAll(probeTasks);
        }
    }
}
