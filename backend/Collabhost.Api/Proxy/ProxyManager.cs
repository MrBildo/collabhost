using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Events;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive -- log interpolation is safe
public class ProxyManager
(
    ICaddyClient caddyClient,
    AppStore appStore,
    ProcessSupervisor processSupervisor,
    IEventBus<ProcessStateChangedEvent> eventBus,
    ProxySettings settings,
    ActivityEventStore activityEventStore,
    ILogger<ProxyManager> logger
) : IHostedService, IDisposable
{
    private readonly ICaddyClient _caddyClient = caddyClient
        ?? throw new ArgumentNullException(nameof(caddyClient));

    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly ProcessSupervisor _processSupervisor = processSupervisor
        ?? throw new ArgumentNullException(nameof(processSupervisor));

    private readonly IEventBus<ProcessStateChangedEvent> _eventBus = eventBus
        ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<ProxyManager> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<string, bool> _routeStates = new(StringComparer.Ordinal);
    private readonly Channel<bool> _syncChannel = Channel.CreateBounded<bool>(1);

    private IDisposable? _subscription;
    private Task? _processorTask;
    private CancellationTokenSource? _shutdownCancellation;
    private string? _proxyAppSlug;
    private bool _disposed;

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
            _logger.LogWarning
            (
                "No proxy app registered -- proxy route sync is disabled. " +
                "Ensure the proxy binary is available and restart Collabhost"
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

        // Enable routes for routing-only apps (e.g. static sites) that have auto-start enabled.
        // Process-based apps are handled by ProcessSupervisor; routing-only apps need route enabling here.
        await EnableAutoStartRoutesAsync(cancellationToken);

        // Check if the proxy process is already running (auto-started before we subscribed)
        var managed = _processSupervisor.GetProcess(proxyApp.Id);

        if (managed is not null && managed.IsRunning)
        {
            _logger.LogInformation("Proxy process already running -- triggering initial route sync");

            RequestSync();
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
        try
        {
            var routeEntries = await LoadRoutableAppsAsync(ct);

            var config = ProxyConfigurationBuilder.Build(routeEntries, _settings);

            var success = await _caddyClient.LoadConfigAsync(config, ct);

            if (success)
            {
                _logger.LogInformation
                (
                    "Proxy routes synced -- {RouteCount} app route(s) + self-route",
                    routeEntries.Count
                );
            }
            else
            {
                _logger.LogWarning("Proxy route sync failed -- config load was rejected");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy route sync failed with exception");
        }
    }

    private async Task ProcessSyncRequestsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _syncChannel.Reader.ReadAllAsync(ct))
            {
                // Small delay to allow the Caddy admin API to become ready after process start
                await Task.Delay(TimeSpan.FromSeconds(2), ct);

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
                _logger.LogInformation("Proxy process is running -- triggering route sync");
                RequestSync();
                break;

            case ProcessState.Crashed:
                _logger.LogWarning("Proxy process crashed -- admin API is unavailable");
                break;

            case ProcessState.Fatal:
                _logger.LogError("Proxy process entered fatal state -- admin API is unavailable");
                break;

            case ProcessState.Stopped:
                _logger.LogInformation("Proxy process stopped -- admin API is unavailable");
                break;

            // Backoff, Starting, Stopping, Restarting -- no route action needed
            case ProcessState.Backoff:
            case ProcessState.Starting:
            case ProcessState.Stopping:
            case ProcessState.Restarting:
                break;
        }
    }

    private async Task<List<RouteEntry>> LoadRoutableAppsAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        var result = new List<RouteEntry>();

        foreach (var app in apps)
        {
            var hasRouting = await _appStore.HasBindingAsync(app.AppTypeId, "routing", ct);

            if (!hasRouting)
            {
                continue;
            }

            var routingConfiguration = await _appStore.ResolveCapabilityAsync<RoutingConfiguration>
            (
                app.AppTypeId,
                app.Id,
                "routing",
                ct
            );

            if (routingConfiguration is null)
            {
                continue;
            }

            int? port = null;

            if (routingConfiguration.ServeMode == ServeMode.ReverseProxy)
            {
                var managedProcess = _processSupervisor.GetProcess(app.Id);
                port = managedProcess?.Port;
            }

            string? artifactDirectory = null;

            if (routingConfiguration.ServeMode == ServeMode.FileServer)
            {
                var artifactConfiguration = await _appStore.ResolveCapabilityAsync<ArtifactConfiguration>
                (
                    app.AppTypeId,
                    app.Id,
                    "artifact",
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
            }

            var enabled = IsRouteEnabled(app.Slug);

            result.Add
            (
                new RouteEntry
                (
                    app.Slug,
                    routingConfiguration.ServeMode,
                    port,
                    routingConfiguration.SpaFallback,
                    artifactDirectory,
                    enabled
                )
            );
        }

        return result;
    }

    private async Task EnableAutoStartRoutesAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        foreach (var app in apps)
        {
            var hasProcess = await _appStore.HasBindingAsync(app.AppTypeId, "process", ct);

            if (hasProcess)
            {
                continue;
            }

            var hasRouting = await _appStore.HasBindingAsync(app.AppTypeId, "routing", ct);

            if (!hasRouting)
            {
                continue;
            }

            var autoStartConfiguration = await _appStore.ResolveCapabilityAsync<AutoStartConfiguration>
            (
                app.AppTypeId, app.Id, "auto-start", ct
            );

            if (autoStartConfiguration is null || !autoStartConfiguration.Enabled)
            {
                continue;
            }

            _logger.LogInformation
            (
                "Auto-start enabling route for routing-only app '{DisplayName}'",
                app.DisplayName
            );

            EnableRoute(app.Slug);

            try
            {
                await _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppAutoStarted,
                        ActorId = ActivityActor.SystemId,
                        ActorName = ActivityActor.SystemName,
                        AppId = app.Id.ToString(null, CultureInfo.InvariantCulture),
                        AppSlug = app.Slug
                    },
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
