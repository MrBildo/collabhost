using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive -- log interpolation is safe
public class ProxyManager
(
    ICaddyClient caddyClient,
    AppStore appStore,
    CapabilityStore capabilityStore,
    TypeStore typeStore,
    ProcessSupervisor processSupervisor,
    IEventBus<ProcessStateChangedEvent> eventBus,
    ProxySettings settings,
    HostingSettings hostingSettings,
    PortalSettings portalSettings,
    ActivityEventStore activityEventStore,
    RuntimeConfigFileWriter runtimeConfigFileWriter,
    AppDataPathResolver dataPathResolver,
    TimeProvider timeProvider,
    ILogger<ProxyManager> logger
) : IHostedService, IDisposable
{
    private readonly ICaddyClient _caddyClient = caddyClient
        ?? throw new ArgumentNullException(nameof(caddyClient));

    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly ProcessSupervisor _processSupervisor = processSupervisor
        ?? throw new ArgumentNullException(nameof(processSupervisor));

    private readonly IEventBus<ProcessStateChangedEvent> _eventBus = eventBus
        ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly HostingSettings _hostingSettings = hostingSettings
        ?? throw new ArgumentNullException(nameof(hostingSettings));

    private readonly PortalSettings _portalSettings = portalSettings
        ?? throw new ArgumentNullException(nameof(portalSettings));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly RuntimeConfigFileWriter _runtimeConfigFileWriter = runtimeConfigFileWriter
        ?? throw new ArgumentNullException(nameof(runtimeConfigFileWriter));

    private readonly AppDataPathResolver _dataPathResolver = dataPathResolver
        ?? throw new ArgumentNullException(nameof(dataPathResolver));

    // Injected so the post-launch admin-API probe loop runs on virtual time under test.
    // Production wires TimeProvider.System; tests wire FakeTimeProvider so the deadline,
    // inter-attempt delay, and per-attempt timeout are all advanced explicitly. Card #258.
    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<ProxyManager> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<string, bool> _routeStates = new(StringComparer.Ordinal);
    private readonly Channel<bool> _syncChannel = Channel.CreateBounded<bool>(1);

    // Backing store is int so Interlocked.CompareExchange can operate directly on it.
    // Writes from the field initializer (Starting), StartAsync disabled path (Disabled),
    // the event handler (Failed / Stopped / Starting), ProbeAndActivateAsync (CAS from Starting),
    // and SyncRoutesAsync (CAS Running<->Degraded on sync failure / recovery, card #217).
    // Reads from /status request threads via CurrentState.
    // Ordering is enforced by Interlocked for CAS and Volatile.Read for the property accessor.
    // Aligned 32-bit reads are atomic in .NET, and Volatile.Read pairs with the Interlocked
    // writes to guarantee visibility across threads.
    private int _currentState = (int)ProxyState.Starting;

    // Latches when the post-launch probe gives up (never-ready within the 5s budget, an
    // unexpected probe exception, or the proxy process going Fatal), suppressing further sync
    // attempts. Distinct from the Degraded state (#217): _proxyDisabled means the admin-API probe
    // never succeeded, so a sync would be pointless; Degraded means the admin API IS reachable but
    // route loads are failing, and the sync loop keeps running so the next attempt can recover.
    //
    // Cleared on the probe-success / CAS-into-Running recovery path (ProbeAndActivateAsync), so a
    // transient proxy outage (e.g. a slow cold start that blew the 5s budget) self-heals once the
    // admin API comes up on a later restart event -- route sync resumes without a Collabhost
    // restart. While the latch is set, ReloadProxyAsync returns a Conflict signal instead of a
    // false success so the operator gets feedback rather than a silent no-op.
    private volatile bool _proxyDisabled;

    // Latest route-sync outcome, surfaced via /api/v1/status as proxyDetail. Replaced
    // wholesale on every sync attempt; a successful sync clears the error fields. Reads
    // are guarded by Volatile.Read to pair with the Volatile.Write on update. Card #217.
    private SyncOutcome _lastSyncOutcome = SyncOutcome.NeverAttempted;

    private IDisposable? _subscription;
    private Task? _processorTask;
    private CancellationTokenSource? _shutdownCancellation;
    private string? _proxyAppSlug;
    private bool _disposed;

    public ProxyState CurrentState => (ProxyState)Volatile.Read(ref _currentState);

    public SyncOutcome LastSyncOutcome => Volatile.Read(ref _lastSyncOutcome);

    // True while the post-launch probe has given up and route sync is suppressed for now.
    // Surfaced so the reload operation can return a Conflict signal instead of a false success
    // while the proxy is disabled. The backing field is volatile, so the read pairs with the
    // probe-path writes for cross-thread visibility.
    public bool IsProxyDisabled => _proxyDisabled;

    public void EnableRoute(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        _routeStates[slug] = true;

        _logger.LogInformation("Route enabled for '{Slug}'", slug);
    }

    public void DisableRoute(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        _routeStates[slug] = false;

        _logger.LogInformation("Route disabled for '{Slug}'", slug);
    }

    public bool IsRouteEnabled(string slug) =>
        !_routeStates.TryGetValue(slug, out var enabled) || enabled;

    public bool IsRouteExplicitlyEnabled(string slug) =>
        _routeStates.TryGetValue(slug, out var enabled) && enabled;

    public void RequestSync() =>
        _syncChannel.Writer.TryWrite(true);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdownCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Find the proxy app by slug
        var proxyApp = await _appStore.GetBySlugAsync("proxy", cancellationToken);

        if (proxyApp is null)
        {
            // CaddyResolver returned null, so ProxyAppSeeder did not register a proxy app.
            // Surface this externally via proxyState='disabled' on /api/v1/status.
            Interlocked.Exchange(ref _currentState, (int)ProxyState.Disabled);

            _logger.LogWarning
            (
                "No proxy app registered -- proxy subsystem disabled. " +
                "No Caddy binary was resolved via COLLABHOST_PROXY_BINARY_PATH or Proxy:BinaryPath. " +
                "proxyState on /api/v1/status will report 'disabled'. " +
                "Re-run the installer to seed the bundled-sidecar path, " +
                "or set COLLABHOST_PROXY_BINARY_PATH and restart Collabhost."
            );

            return;
        }

        _proxyAppSlug = proxyApp.Slug;

        // Start the background channel processor
        _processorTask = ProcessSyncRequestsAsync(_shutdownCancellation.Token);

        // Subscribe to process state changes
        _subscription = _eventBus.Subscribe(OnProcessStateChanged);

        _logger.LogInformation
        (
            "Proxy manager started -- listening for process state changes (proxy app: {Slug})",
            _proxyAppSlug
        );

        // Restore persisted operator-stopped route state for routing-only apps BEFORE
        // any auto-start or sync runs. Without this, IsRouteEnabled returns true via
        // the default-true fallback (line above) and routes the operator disabled
        // pre-restart re-emit as live. Process-bearing routed apps are scoped out --
        // their "stopped" signal is 502 from a dead upstream, not a torn-down route.
        // Card #350.
        await HydrateRouteStatesFromPersistenceAsync(cancellationToken);

        // Enable routes for routing-only apps (e.g. static sites) that have auto-start enabled.
        // Process-based apps are handled by ProcessSupervisor; routing-only apps need route enabling here.
        await EnableAutoStartRoutesAsync(cancellationToken);

        // Check if the proxy process is already running (auto-started before we subscribed).
        // Run the probe the same way we would on a Running-event transition so that
        // CurrentState converges correctly even when the process beat us to startup.
        var managed = _processSupervisor.GetProcess(proxyApp.Id);

        if (managed is not null && managed.IsRunning)
        {
            _logger.LogInformation("Proxy process already running -- probing admin API readiness");

#pragma warning disable VSTHRD110, MA0134, CS4014, CA2016, MA0040
            Task.Run(() => ProbeAndActivateAsync(_shutdownCancellation.Token));
#pragma warning restore VSTHRD110, MA0134, CS4014, CA2016, MA0040
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;

        _syncChannel.Writer.TryComplete();

        if (_processorTask is not null)
        {
            await _shutdownCancellation!.CancelAsync();

            try
            {
                // Awaiting the background processor started in StartAsync -- intentional shutdown coordination
#pragma warning disable VSTHRD003
                await _processorTask;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }

        _logger.LogInformation("Proxy manager stopped");
    }

    public async Task SyncRoutesAsync(CancellationToken ct = default)
    {
        if (_proxyDisabled)
        {
            return;
        }

        try
        {
            var routeEntries = await LoadRoutableAppsAsync(ct);

            var config = ProxyConfigurationBuilder.Build(routeEntries, _settings, _hostingSettings, _portalSettings);

            var result = await _caddyClient.LoadConfigAsync(config, ct);

            if (result.Success)
            {
                Volatile.Write(ref _lastSyncOutcome, SyncOutcome.Succeeded(DateTime.UtcNow));
                TryRecoverFromDegraded();

                _logger.LogInformation
                (
                    "Proxy routes synced -- {RouteCount} app route(s) + self-route",
                    routeEntries.Count
                );
            }
            else
            {
                var errorDetail = FormatSyncError(result);

                Volatile.Write
                (
                    ref _lastSyncOutcome,
                    SyncOutcome.Failed(DateTime.UtcNow, errorDetail)
                );

                TryEnterDegraded();

                _logger.LogWarning
                (
                    "Proxy route sync failed -- {Error} (proxyState may be 'degraded')",
                    errorDetail
                );
            }
        }
        catch (Exception ex)
        {
            Volatile.Write
            (
                ref _lastSyncOutcome,
                SyncOutcome.Failed(DateTime.UtcNow, ex.Message)
            );

            TryEnterDegraded();

            _logger.LogError(ex, "Proxy route sync failed with exception (proxyState may be 'degraded')");
        }
    }

    // Format the LoadConfigResult into an operator-readable error string. Caddy's load
    // response body usually carries a structured JSON error; pass it through verbatim so
    // the operator sees the real bind/parse/issuer message without us re-encoding it.
    private static string FormatSyncError(LoadConfigResult result)
    {
        var hasBody = !string.IsNullOrWhiteSpace(result.ErrorBody);
        var hasStatus = result.StatusCode is not null;

        return (hasStatus, hasBody) switch
        {
            (true, true) => string.Create
            (
                CultureInfo.InvariantCulture,
                $"Caddy admin API returned {result.StatusCode}: {result.ErrorBody!.Trim()}"
            ),
            (false, true) => result.ErrorBody!.Trim(),
            (true, false) => string.Create
            (
                CultureInfo.InvariantCulture,
                $"Caddy admin API returned {result.StatusCode}"
            ),
            _ => "Caddy admin API rejected the route configuration"
        };
    }

    // CAS Running -> Degraded. Only transitions out of Running so a Stopping/Failed/Disabled
    // event can't be overwritten by a sync that lost the race. Card #217.
    private void TryEnterDegraded()
    {
        var previous = (ProxyState)Interlocked.CompareExchange
        (
            ref _currentState,
            (int)ProxyState.Degraded,
            (int)ProxyState.Running
        );

        if (previous == ProxyState.Running)
        {
            _logger.LogWarning("Proxy entered degraded state -- routes are not reaching the public listener");
        }
    }

    // CAS Degraded -> Running. Only transitions out of Degraded so a parallel terminal-state
    // event keeps its write. Card #217 recovery edge.
    private void TryRecoverFromDegraded()
    {
        var previous = (ProxyState)Interlocked.CompareExchange
        (
            ref _currentState,
            (int)ProxyState.Running,
            (int)ProxyState.Degraded
        );

        if (previous == ProxyState.Degraded)
        {
            _logger.LogInformation("Proxy recovered from degraded state -- routes are reaching the public listener");
        }
    }

    // Post-launch admin-API probe. Soft-fail with visibility:
    // on success -> ProxyState.Running and route sync is activated.
    // on timeout -> ProxyState.Failed, proxy subsystem disabled for this process lifetime,
    //              and loud error log pointing at COLLABHOST_PROXY_BINARY_PATH / Proxy:BinaryPath.
    //
    // All time work flows through the injected TimeProvider so tests can advance a virtual
    // clock instead of waiting on wall-clock thread-pool timers. Card #258 -- Windows-CI
    // flake on the slow-start assertion was caused by Task.Delay accuracy under contention
    // pushing the assertion past its 3s wall-clock budget.
    internal async Task<bool> VerifyCaddyReadyAsync(CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow().AddSeconds(5);
        var perAttemptTimeout = TimeSpan.FromSeconds(1);

        while (_timeProvider.GetUtcNow() < deadline && !ct.IsCancellationRequested)
        {
            // The per-attempt CTS has its own time-provider-aware delay; linking with the
            // outer ct gives the same effect as the previous CreateLinkedTokenSource +
            // CancelAfter combination, but with the per-attempt delay routed through
            // _timeProvider so virtual time advances trigger the per-attempt timeout under test.
            using var perAttemptCts = new CancellationTokenSource(perAttemptTimeout, _timeProvider);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, perAttemptCts.Token);

            try
            {
                // TimeoutException / HttpRequestException are caught inside IsReadyAsync
                // (returns false). Per-attempt deadline hits here via the linked CTS --
                // when that fires, IsReadyAsync's `when (!ct.IsCancellationRequested)`
                // filter rejects the cancellation (the linked token IS cancelled) and
                // TaskCanceledException propagates out. That's a per-attempt timeout, not
                // a caller-cancellation: swallow it here and loop to the next attempt.
                // Outer `ct` cancellation is re-raised below so the probe exits cleanly
                // on shutdown.
                if (await _caddyClient.IsReadyAsync(linkedCts.Token))
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-attempt timeout tripped the linked CTS. Fall through to the inter-
                // attempt delay and try again until the 5s budget exhausts.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), _timeProvider, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private async Task ProcessSyncRequestsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _syncChannel.Reader.ReadAllAsync(ct))
            {
                if (_proxyDisabled)
                {
                    continue;
                }

                await SyncRoutesAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private void OnProcessStateChanged(ProcessStateChangedEvent processEvent)
    {
        if (_proxyAppSlug is null)
        {
            return;
        }

        if (string.Equals(processEvent.AppSlug, _proxyAppSlug, StringComparison.Ordinal))
        {
            HandleProxyAppStateChange(processEvent);

            return;
        }

        // When any non-proxy app transitions to Running, re-sync so Caddy picks up the new port
        if (processEvent.NewState == ProcessState.Running)
        {
            RequestSync();
        }
    }

    private void HandleProxyAppStateChange(ProcessStateChangedEvent processEvent)
    {
        switch (processEvent.NewState)
        {
            case ProcessState.Running:
                // Re-enter the Starting window before spawning the probe so /status reports
                // 'starting' during the ≤5s probe budget -- otherwise a restart from a terminal
                // state (Stopped/Failed) would keep reporting the old state until the probe
                // finishes. Unconditional Exchange: a fresh Running event is the authoritative
                // signal that the probe window has opened (HIGH-1).
                Interlocked.Exchange(ref _currentState, (int)ProxyState.Starting);
                _logger.LogInformation("Proxy process is running -- probing admin API readiness (proxyState='starting')");
                // Fire-and-forget: probe runs off the event-bus callback so we don't block
                // the supervisor. State transitions + logging happen inside.
#pragma warning disable VSTHRD110, MA0134, CS4014, CA2016, MA0040
                Task.Run(() => ProbeAndActivateAsync(_shutdownCancellation?.Token ?? CancellationToken.None));
#pragma warning restore VSTHRD110, MA0134, CS4014, CA2016, MA0040
                break;

            case ProcessState.Crashed:
                Interlocked.Exchange(ref _currentState, (int)ProxyState.Failed);
                _logger.LogWarning("Proxy process crashed -- admin API is unavailable (proxyState='failed')");
                break;

            case ProcessState.Fatal:
                Interlocked.Exchange(ref _currentState, (int)ProxyState.Failed);
                _proxyDisabled = true;
                _logger.LogError("Proxy process entered fatal state -- admin API is unavailable (proxyState='failed')");
                break;

            case ProcessState.Stopped:
                // Operator stopped the proxy via UI/API (host shutdown uses StopAsync, not an event).
                Interlocked.Exchange(ref _currentState, (int)ProxyState.Stopped);
                _logger.LogInformation("Proxy process stopped -- admin API is unavailable (proxyState='stopped')");
                break;

            // Backoff, Starting, Stopping, Restarting -- no state transition needed
            case ProcessState.Backoff:
            case ProcessState.Starting:
            case ProcessState.Stopping:
            case ProcessState.Restarting:
                break;
        }
    }

    // Runs on a Task.Run thread spawned from the event handler or StartAsync. All state writes
    // are CAS-from-Starting per MED-1. The probe owns transitions out of the Starting window
    // and nothing else. If a Crashed, Fatal, or Stopped event moved the state to a terminal
    // value while the probe was mid-poll, the CAS fails and the probe's late write is discarded.
    // The outer Exception catch guarantees any unexpected exception still leaves the subsystem
    // in a defensive terminal state instead of silently disappearing into an unobserved task.
    internal async Task ProbeAndActivateAsync(CancellationToken ct)
    {
        try
        {
            var ready = await VerifyCaddyReadyAsync(ct);

            if (ready)
            {
                var previous = (ProxyState)Interlocked.CompareExchange
                (
                    ref _currentState,
                    (int)ProxyState.Running,
                    (int)ProxyState.Starting
                );

                if (previous != ProxyState.Starting)
                {
                    _logger.LogInformation
                    (
                        "Caddy admin API became ready but state already transitioned to {State} -- " +
                        "leaving state as-is (late probe write suppressed)",
                        previous
                    );

                    return;
                }

                // Clear the disabled latch on the recovery path BEFORE requesting the sync, so the
                // enqueued sync is not dropped as a no-op by the still-set latch. A transient outage
                // (slow cold start that blew the 5s budget, then a later restart event re-opening
                // the probe window) self-heals here -- route sync resumes without a Collabhost
                // restart. Only the won-CAS-from-Starting arm clears it: a terminal event (Fatal/
                // Crashed/Stopped) that landed mid-probe keeps the latch, since the proxy is not
                // actually back.
                _proxyDisabled = false;

                _logger.LogInformation("Caddy admin API is ready -- proxy subsystem activated (proxyState='running')");
                RequestSync();
                return;
            }

            // Soft-fail with visibility. CAS from Starting so that a Crashed/Fatal
            // event landing during the probe window keeps the event-handler's terminal write.
            var failedFromStarting = Interlocked.CompareExchange
            (
                ref _currentState,
                (int)ProxyState.Failed,
                (int)ProxyState.Starting
            ) == (int)ProxyState.Starting;

            _proxyDisabled = true;

            if (!failedFromStarting)
            {
                _logger.LogWarning
                (
                    "Caddy admin API did not become ready within 5s, but state already transitioned " +
                    "to {State} -- proxy subsystem disabled; leaving state as-is.",
                    CurrentState
                );

                return;
            }

            _logger.LogError
            (
                "Caddy admin API did not become ready within 5s -- proxy subsystem disabled for this process lifetime " +
                "(proxyState='failed'). Check Caddy logs, verify COLLABHOST_PROXY_BINARY_PATH or Proxy:BinaryPath, " +
                "and restart Collabhost. The registry, supervisor, dashboard, and managed-app operations continue to function; " +
                "HTTPS routing to {{slug}}.{BaseDomain} is offline until the proxy is restored.",
                _settings.BaseDomain
            );
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            // MED-2: an unexpected exception in the fire-and-forget probe would otherwise vanish
            // into TaskScheduler.UnobservedTaskException. Transition to the most defensive
            // terminal state (Failed + disabled) so /status reflects reality.
            Interlocked.Exchange(ref _currentState, (int)ProxyState.Failed);
            _proxyDisabled = true;

            _logger.LogError(ex, "Proxy probe aborted unexpectedly -- proxy subsystem disabled (proxyState='failed')");
        }
    }

    private async Task<List<RouteEntry>> LoadRoutableAppsAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        var result = new List<RouteEntry>();

        foreach (var app in apps)
        {
            var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

            if (!hasRouting)
            {
                continue;
            }

            var routingConfiguration = await _capabilityStore.ResolveAsync<RoutingConfiguration>
            (
                "routing",
                app,
                ct
            );

            if (routingConfiguration is null)
            {
                continue;
            }

            int? port = null;
            string? externalDial = null;
            string? externalScheme = null;

            if (routingConfiguration.ServeMode == ServeMode.ReverseProxy)
            {
                // External-route apps (Card #348): the upstream is operator-
                // declared, not Collabhost-supervised. Mirrors the static-site
                // "artifact location is not configured" skip-with-warning
                // shape so misconfigured external-route apps surface the same
                // way to the operator -- the route disappears from the proxy
                // config and the warning hits the log.
                if (_typeStore.HasBinding(app.AppTypeSlug, "external-target"))
                {
                    var target = await _capabilityStore.ResolveAsync<ExternalTargetConfiguration>
                    (
                        "external-target", app, ct
                    );

                    if (target is null
                        || string.IsNullOrWhiteSpace(target.Host)
                        || target.Port <= 0)
                    {
                        _logger.LogWarning
                        (
                            "Skipping route for '{Slug}' -- external-target host or port is not configured",
                            app.Slug
                        );

                        continue;
                    }

                    externalDial = string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "{0}:{1}",
                        target.Host,
                        target.Port
                    );

                    externalScheme = target.Scheme;
                }
                else
                {
                    var managedProcess = _processSupervisor.GetProcess(app.Id);
                    port = managedProcess?.Port;
                }
            }

            string? artifactDirectory = null;
            IReadOnlyDictionary<string, string>? responseHeaders = null;
            string? runtimeConfigFilePath = null;
            string? runtimeConfigWritableRoot = null;

            if (routingConfiguration.ServeMode == ServeMode.FileServer)
            {
                var artifactConfiguration = await _capabilityStore.ResolveAsync<ArtifactConfiguration>
                (
                    "artifact",
                    app,
                    ct
                );

                artifactDirectory = artifactConfiguration?.Location;

                if (string.IsNullOrWhiteSpace(artifactDirectory))
                {
                    _logger.LogWarning
                    (
                        "Skipping route for '{Slug}' -- artifact location is not configured",
                        app.Slug
                    );

                    continue;
                }

                responseHeaders = routingConfiguration.ResponseHeaders.Count > 0
                    ? new Dictionary<string, string>(routingConfiguration.ResponseHeaders, StringComparer.Ordinal)
                    : null;

                // Runtime-config-file overlay (#369). When the type declares the
                // capability, the builder emits a path-scoped overlay that serves
                // the config path from the writable data dir ahead of the
                // artifact-rooted file_server. The writer renders into the same
                // writable dir, so the served copy survives an artifact-dir
                // redeploy. Both null when the capability is absent -> no overlay
                // emission (the migration-safe default).
                if (_typeStore.HasBinding(app.AppTypeSlug, "runtime-config-file"))
                {
                    var runtimeConfig = await _capabilityStore.ResolveAsync<RuntimeConfigFileConfiguration>
                    (
                        "runtime-config-file",
                        app,
                        ct
                    );

                    if (runtimeConfig is not null && !string.IsNullOrWhiteSpace(runtimeConfig.Path))
                    {
                        runtimeConfigFilePath = NormalizeConfigPath(runtimeConfig.Path);
                        runtimeConfigWritableRoot = _dataPathResolver.ResolveFor(app.Slug);
                    }
                }
            }

            // Resolve security-headers for every routed app type (Card #309).
            // The capability is bound on every routed type's JSON with the
            // XCTO seed; system-service has no routing binding so this code
            // is never reached for it. A type without the security-headers
            // binding returns null and the builder skips emission.
            var hasSecurityHeaders = _typeStore.HasBinding(app.AppTypeSlug, "security-headers");

            SecurityHeadersConfiguration? securityHeaders = null;

            if (hasSecurityHeaders)
            {
                securityHeaders = await _capabilityStore.ResolveAsync<SecurityHeadersConfiguration>
                (
                    "security-headers",
                    app,
                    ct
                );
            }

            var enabled = IsRouteEnabled(app.Slug);

            // Resolve the operator-configurable DomainPattern at the boundary so the builder
            // consumes a pre-resolved hostname rather than recomputing. Closes the half-wired
            // bug where DomainPattern surfaced in the routes table but was ignored by the
            // emitted Caddy config.
            var domain = CapabilityResolver.ResolveDomain
            (
                routingConfiguration.DomainPattern,
                app.Slug,
                _settings.BaseDomain
            );

            result.Add
            (
                new RouteEntry
                (
                    app.Slug,
                    domain,
                    routingConfiguration.ServeMode,
                    port,
                    routingConfiguration.SpaFallback,
                    artifactDirectory,
                    enabled,
                    responseHeaders,
                    securityHeaders,
                    externalDial,
                    externalScheme,
                    runtimeConfigFilePath,
                    runtimeConfigWritableRoot
                )
            );
        }

        return result;
    }

    // Normalize the runtime-config-file path to the leading-slash form Caddy's
    // `path` matcher expects (e.g. "config.json" -> "/config.json"). The
    // capability default is already "/config.json"; an operator-edited value
    // without a leading slash is tolerated here so the overlay matches. Mirrors
    // the leading-slash convention #308 uses for ResponseHeaders match keys.
    private static string NormalizeConfigPath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

    // Hydrate _routeStates from the persisted App.StoppedByOperator column at boot. Only
    // routing-only AppTypes are in scope: process-bearing routed apps (dotnet-app,
    // nodejs-app) keep their Caddy route up across operator-stop and rely on a 502 from
    // the dead upstream as the operator-visible "stopped" signal. Only `false` entries are written -- enabled
    // apps stay implicit via IsRouteEnabled's default-true fallback (line 131), so the
    // dict only grows by the count of operator-stopped routing-only apps. The auto-start
    // skip for process-bearing apps lives in ProcessSupervisor.StartAsync. Card #350.
    private async Task HydrateRouteStatesFromPersistenceAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        foreach (var app in apps)
        {
            // Process-bearing routed types keep route up on operator-stop (502 signal).
            if (_typeStore.HasBinding(app.AppTypeSlug, "process"))
            {
                continue;
            }

            if (!_typeStore.HasBinding(app.AppTypeSlug, "routing"))
            {
                continue;
            }

            if (app.StoppedByOperator)
            {
                _routeStates[app.Slug] = false;

                _logger.LogInformation
                (
                    "Hydrated route-state for '{Slug}' -- disabled (operator stopped before restart)",
                    app.Slug
                );
            }
        }
    }

    private async Task EnableAutoStartRoutesAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        foreach (var app in apps)
        {
            var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");

            if (hasProcess)
            {
                continue;
            }

            var hasRouting = _typeStore.HasBinding(app.AppTypeSlug, "routing");

            if (!hasRouting)
            {
                continue;
            }

            var autoStartConfiguration = await _capabilityStore.ResolveAsync<AutoStartConfiguration>
            (
                "auto-start", app, ct
            );

            if (autoStartConfiguration is null || !autoStartConfiguration.Enabled)
            {
                continue;
            }

            // Defensive symmetry with ProcessSupervisor.StartAsync's StoppedByOperator
            // skip. No built-in routing-only type today carries auto-start.enabled=true,
            // but operator-defined types could -- and the hydration above already wrote
            // _routeStates[slug] = false, so without this skip we would log "auto-start
            // enabling" and then immediately re-disable through the hydrated dict on the
            // next sync. Keep the boot log honest. Card #350.
            if (app.StoppedByOperator)
            {
                _logger.LogInformation
                (
                    "Skipping auto-start of route for '{DisplayName}' -- stopped by operator before restart",
                    app.DisplayName
                );
                continue;
            }

            _logger.LogInformation
            (
                "Auto-start enabling route for routing-only app '{DisplayName}'",
                app.DisplayName
            );

            // Render runtime-config-file BEFORE enabling the route (Card #336).
            // Writer no-ops when resolved Values is empty. If the write fails on
            // a non-empty Values (e.g. artifact dir missing on disk yet), log a
            // warning and skip the route -- the operator-visible signal is the
            // route NOT enabling, matching the user-initiated StartAppAsync
            // failure semantics. Auto-start does not surface to a caller, so
            // failure goes to the log + activity stream.
            try
            {
                await _runtimeConfigFileWriter.RenderAsync(app, ct);
            }
            catch (RuntimeConfigFileWriteException ex)
            {
                _logger.LogWarning
                (
                    ex,
                    "Auto-start of route for '{DisplayName}' skipped -- runtime-config-file write failed",
                    app.DisplayName
                );

                continue;
            }

            EnableRoute(app.Slug);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    ActivityEvent.ForSystem(ActivityEventTypes.AppAutoStarted, app.Id, app.Slug),
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", app.DisplayName);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _subscription?.Dispose();
        _shutdownCancellation?.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
#pragma warning restore MA0076
