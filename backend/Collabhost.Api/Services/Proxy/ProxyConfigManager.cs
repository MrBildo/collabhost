using System.Collections.Concurrent;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Services.Proxy;

public sealed class ProxyConfigManager
(
    IProxyConfigClient proxyClient,
    ProxyConfigGenerator generator,
    IServiceScopeFactory scopeFactory,
    IProcessStateEventBus processStateEventBus,
    ProcessSupervisor processSupervisor,
    ProxySettings settings,
    ILogger<ProxyConfigManager> logger
) : IHostedService
{
    private readonly IProxyConfigClient _proxyClient = proxyClient ?? throw new ArgumentNullException(nameof(proxyClient));
    private readonly ProxyConfigGenerator _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IProcessStateEventBus _processStateEventBus = processStateEventBus ?? throw new ArgumentNullException(nameof(processStateEventBus));
    private readonly ProcessSupervisor _processSupervisor = processSupervisor ?? throw new ArgumentNullException(nameof(processSupervisor));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<ProxyConfigManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<string, bool> _routeEnabledStates = new(StringComparer.Ordinal);

    private Guid? _proxyAppId;
    private IDisposable? _subscription;
    private bool _disabled;

#pragma warning disable MA0051 // Long method justified — startup with proxy app lookup and event subscription
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        var proxyApp = await db.Database
            .SqlQuery<ProxyAppLookup>
            (
                $"""
                SELECT
                    A.[Id]
                FROM
                    [App] A
                WHERE
                    A.[Name] = 'proxy'
                """
            )
            .SingleOrDefaultAsync(cancellationToken);

        if (proxyApp is null)
        {
            _logger.LogWarning
            (
                "No proxy app registered — proxy route sync is disabled. " +
                "Ensure the proxy binary is available and restart Collabhost"
            );
            _disabled = true;
            return;
        }

        _proxyAppId = proxyApp.Id;

        _subscription = _processStateEventBus.Subscribe(OnProcessStateChanged);

        _logger.LogInformation
        (
            "Proxy config manager started — listening for proxy app state changes (AppId: {AppId})",
            _proxyAppId
        );

        // Check if the proxy process is already running (race condition: ProcessSupervisor
        // may have auto-started and published the Running event before we subscribed)
        var managed = _processSupervisor.GetProcess(_proxyAppId.Value);
        if (managed is not null && managed.IsRunning)
        {
            _logger.LogInformation("Proxy process already running — triggering initial route sync");

            // Background route sync is intentional — StartAsync should not block on HTTP calls to Caddy.
            // The task is self-contained with full error handling inside HandleProxyRunningAsync.
#pragma warning disable CS4014, VSTHRD110, MA0134 // Intentional fire-and-forget from IHostedService.StartAsync
            Task.Run(async () => await HandleProxyRunningAsync(), CancellationToken.None);
#pragma warning restore CS4014, VSTHRD110, MA0134
        }
    }
#pragma warning restore MA0051

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        _subscription = null;

        _logger.LogInformation("Proxy config manager stopped");

        return Task.CompletedTask;
    }

    public async Task SyncRoutesAsync(CancellationToken ct = default)
    {
        try
        {
            var apps = await LoadRoutableAppsAsync(ct);
            var config = _generator.Generate(apps);
            var success = await _proxyClient.LoadConfigAsync(config, ct);

            if (success)
            {
                _logger.LogInformation
                (
                    "Proxy routes synced — {RouteCount} app routes + self-route",
                    apps.Count
                );
            }
            else
            {
                _logger.LogWarning("Proxy route sync failed — config load was rejected");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Proxy route sync failed with exception");
        }
    }

    public void EnableRoute(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        _routeEnabledStates[slug] = true;
        _logger.LogInformation("Route enabled for '{Slug}'", slug);
    }

    public void DisableRoute(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        _routeEnabledStates[slug] = false;
        _logger.LogInformation("Route disabled for '{Slug}'", slug);
    }

    public bool IsRouteEnabled(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        // Routes are enabled by default
        return !_routeEnabledStates.TryGetValue(slug, out var enabled) || enabled;
    }

    private void OnProcessStateChanged(ProcessStateChangedEvent processEvent)
    {
        if (_disabled || _proxyAppId is null)
        {
            return;
        }

        if (processEvent.AppId == _proxyAppId)
        {
            HandleProxyAppEvent(processEvent);
            return;
        }

        // When any non-proxy app transitions to Running, re-sync routes so Caddy
        // picks up the newly assigned port for reverse-proxy routes.
        if (processEvent.NewStateId == IdentifierCatalog.ProcessStates.Running)
        {
            HandleAppRunning(processEvent);
        }
    }

    private void HandleProxyAppEvent(ProcessStateChangedEvent processEvent)
    {
        if (processEvent.NewStateId == IdentifierCatalog.ProcessStates.Running)
        {
            // Background route sync is intentional — OnProcessStateChanged is a synchronous
            // event callback and cannot await. The task is self-contained with full error handling.
#pragma warning disable VSTHRD110, MA0134 // Intentional fire-and-forget from synchronous event callback
            Task.Run(async () => await HandleProxyRunningAsync());
#pragma warning restore VSTHRD110, MA0134
        }
        else if (processEvent.NewStateId == IdentifierCatalog.ProcessStates.Crashed)
        {
            _logger.LogWarning("Proxy process crashed — admin API is unavailable");
        }
        else if (processEvent.NewStateId == IdentifierCatalog.ProcessStates.Stopped)
        {
            _logger.LogInformation("Proxy process stopped — admin API is unavailable");
        }
    }

    private void HandleAppRunning(ProcessStateChangedEvent processEvent) =>
        // Background route sync is intentional — OnProcessStateChanged is a synchronous
        // event callback and cannot await. The task is self-contained with full error handling.
#pragma warning disable VSTHRD110, MA0134 // Intentional fire-and-forget from synchronous event callback
        Task.Run(async () =>
        {
            try
            {
                var hasReverseProxyRouting = await HasReverseProxyRoutingAsync(processEvent.AppId);

                if (!hasReverseProxyRouting)
                {
                    return;
                }

                _logger.LogInformation
                (
                    "App '{AppExternalId}' is now running — re-syncing proxy routes for updated port",
                    processEvent.AppExternalId
                );

                await SyncRoutesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError
                (
                    ex,
                    "Failed to re-sync routes after app '{AppExternalId}' started",
                    processEvent.AppExternalId
                );
            }
        });
#pragma warning restore VSTHRD110, MA0134

    private async Task HandleProxyRunningAsync()
    {
        _logger.LogInformation
        (
            "Proxy process is running — waiting for admin API at {Url}",
            _settings.AdminApiUrl
        );

        // Wait for admin API to be ready
        await Task.Delay(TimeSpan.FromSeconds(2));

        try
        {
            await SyncRoutesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "First route sync attempt failed — retrying after delay");

            await Task.Delay(TimeSpan.FromSeconds(2));

            try
            {
                await SyncRoutesAsync();
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "Retry route sync also failed — routes may be stale");
            }
        }
    }

    private async Task<bool> HasReverseProxyRoutingAsync(Guid appId)
    {
        using var scope = _scopeFactory.CreateScope();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<ICapabilityResolver>();

        var routingConfiguration = await capabilityResolver.ResolveAsync<RoutingConfiguration>
        (
            appId, IdentifierCatalog.Capabilities.Routing
        );

        return routingConfiguration is not null
            && string.Equals(routingConfiguration.ServeMode, StringCatalog.ServeModes.ReverseProxy, StringComparison.OrdinalIgnoreCase);
    }

#pragma warning disable MA0051 // Long method justified — loading routable apps with capability resolution
    private async Task<List<AppRouteInfo>> LoadRoutableAppsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<ICapabilityResolver>();

        // Load apps that have a routing capability
        var appIds = await db.Database
            .SqlQuery<RoutableAppRow>
            (
                $"""
                SELECT
                    A.[Id]
                    ,A.[Name] AS [Slug]
                FROM
                    [App] A
                    INNER JOIN [AppTypeCapability] ATC ON ATC.[AppTypeId] = A.[AppTypeId]
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                WHERE
                    C.[Slug] = {StringCatalog.Capabilities.Routing}
                """
            )
            .ToListAsync(ct);

        var result = new List<AppRouteInfo>();

        foreach (var row in appIds)
        {
            var routingConfiguration = await capabilityResolver.ResolveAsync<RoutingConfiguration>
            (
                row.Id, IdentifierCatalog.Capabilities.Routing, ct
            );

            if (routingConfiguration is null)
            {
                continue;
            }

            // Resolve port from in-memory process state for reverse proxy routes
            int? port = null;
            if (string.Equals(routingConfiguration.ServeMode, StringCatalog.ServeModes.ReverseProxy, StringComparison.OrdinalIgnoreCase))
            {
                var managedProcess = _processSupervisor.GetProcess(row.Id);
                port = managedProcess?.Port;
            }

            var spaFallback = routingConfiguration.SpaFallback ?? false;

            // Resolve artifact location for file-server routes
            string? artifactLocation = null;
            if (string.Equals(routingConfiguration.ServeMode, StringCatalog.ServeModes.FileServer, StringComparison.OrdinalIgnoreCase))
            {
                var artifactConfiguration = await capabilityResolver.ResolveAsync<ArtifactConfiguration>
                (
                    row.Id, IdentifierCatalog.Capabilities.Artifact, ct
                );

                artifactLocation = artifactConfiguration?.Location;

                if (string.IsNullOrWhiteSpace(artifactLocation))
                {
                    _logger.LogWarning
                    (
                        "Skipping route for '{Slug}' — artifact location is not configured",
                        row.Slug
                    );
                    continue;
                }
            }

            // Check if route is disabled
            if (!IsRouteEnabled(row.Slug))
            {
                result.Add(new AppRouteInfo(row.Slug, "disabled", port, spaFallback, artifactLocation));
                continue;
            }

            result.Add(new AppRouteInfo(row.Slug, routingConfiguration.ServeMode, port, spaFallback, artifactLocation));
        }

        return result;
    }
#pragma warning restore MA0051

    private sealed record ProxyAppLookup(Guid Id);

    private sealed record RoutableAppRow(Guid Id, string Slug);
}
