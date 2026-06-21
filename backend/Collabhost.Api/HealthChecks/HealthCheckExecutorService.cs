using System.Collections.Concurrent;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
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
    ProxyManager proxy,
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

    // External-route apps (Card #348) don't have a supervised process for the
    // executor to gate on -- the route-enabled state is the analog. Resolved
    // via ProxyManager.IsRouteEnabled inside TickAsync.
    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

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

    // PLT-01: drop a deleted app's cached health state. The tick prunes lazily by walking the LIVE
    // app set (ListAsync), so a deleted app -- gone from that set -- is never visited and its
    // _latest/_lastProbedAt entries would leak forever. DeleteAppOperation calls this on delete.
    public void Remove(Ulid appId)
    {
        _latest.TryRemove(appId, out _);
        _lastProbedAt.TryRemove(appId, out _);
    }

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

            // Resolve the probe target. Two shapes today (Card #348):
            //   (a) Supervised-process apps (dotnet-app, nodejs-app, executable):
            //       the supervisor's ManagedProcess is the source of truth for
            //       port + running-state; the probe dials localhost:{port}.
            //   (b) External-target apps (external-route): the operator-declared
            //       host:port is the probe target; the route-enabled state is
            //       the analog of "process is running" -- a disabled route
            //       means the operator has stopped the app and we should NOT
            //       probe the underlying upstream (which may still be up).
            string host;
            int port;
            string scheme;

            var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");

            if (hasProcess)
            {
                var process = _supervisor.GetProcess(app.Id);

                if (process is null || !process.IsRunning || process.Port is null)
                {
                    // Not running -> clear stale "healthy" / "unhealthy" results.
                    // The reader returns null in this state and the UI renders "--".
                    _latest.TryRemove(app.Id, out _);
                    _lastProbedAt.TryRemove(app.Id, out _);
                    continue;
                }

                host = "localhost";
                port = process.Port.Value;
                scheme = "http";
            }
            else if (_typeStore.HasBinding(app.AppTypeSlug, "external-target"))
            {
                // Disabled route is the "operator stopped this app" signal --
                // clear the cached result and skip. Matches the supervised-
                // process branch's "not running" clearing semantics so the FE
                // renders "--" on the App Detail page.
                if (!_proxy.IsRouteEnabled(app.Slug))
                {
                    _latest.TryRemove(app.Id, out _);
                    _lastProbedAt.TryRemove(app.Id, out _);
                    continue;
                }

                var target = await _capabilityStore.ResolveAsync<ExternalTargetConfiguration>
                (
                    "external-target", app, ct
                );

                if (target is null
                    || string.IsNullOrWhiteSpace(target.Host)
                    || target.Port <= 0)
                {
                    // Misconfigured external-target -> clear and skip. Same
                    // shape as supervised-process unconfigured-port -- the
                    // proxy's LoadRoutableAppsAsync logs the warning; here we
                    // just keep state clean.
                    _latest.TryRemove(app.Id, out _);
                    _lastProbedAt.TryRemove(app.Id, out _);
                    continue;
                }

                host = target.Host;
                port = target.Port;
                scheme = target.Scheme;
            }
            else
            {
                // Health-check capability bound but neither process nor
                // external-target -- unreachable for any built-in AppType
                // shipped today; the safe behavior is "skip and clear" so a
                // misshaped user-defined AppType doesn't accumulate stale
                // probe state.
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

            var appId = app.Id;
            var slug = app.Slug;
            var probeHost = host;
            var probePort = port;
            var probeScheme = scheme;

            probeTasks.Add(Task.Run(async () =>
            {
                var result = await _probe.ProbeAsync(slug, probeHost, probePort, probeScheme, configuration, ct);
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
