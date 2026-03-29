using Collabhost.Api.Domain.Catalogs;

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

    private Guid? _proxyAppId;
    private IDisposable? _subscription;
    private bool _disabled;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var proxyServiceTypeId = IdentifierCatalog.AppTypes.ProxyService;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        var proxyApp = await db.Database
            .SqlQuery<ProxyAppLookup>(
                $"""
                SELECT
                    A.[Id]
                FROM
                    [App] A
                WHERE
                    A.[AppTypeId] = {proxyServiceTypeId}
                """)
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
        var managed = _processSupervisor.GetStatus(_proxyAppId.Value);
        if (managed is not null && managed.IsRunning)
        {
            _logger.LogInformation("Proxy process already running — triggering initial route sync");
            _ = Task.Run(async () => await HandleProxyRunningAsync(), CancellationToken.None);
        }
    }

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

    private void OnProcessStateChanged(ProcessStateChangedEvent processEvent)
    {
        if (_disabled || _proxyAppId is null)
        {
            return;
        }

        if (processEvent.AppId != _proxyAppId)
        {
            return;
        }

        if (processEvent.NewStateId == IdentifierCatalog.ProcessStates.Running)
        {
            _ = Task.Run(async () => await HandleProxyRunningAsync());
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

    private async Task<List<AppRouteInfo>> LoadRoutableAppsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        var allApps = await db.Database
            .SqlQuery<AppRouteInfo>(
                $"""
                SELECT
                    A.[Name] AS [Slug]
                    ,A.[AppTypeId]
                    ,A.[Port]
                    ,A.[InstallDirectory]
                    ,A.[HealthEndpoint]
                FROM
                    [App] A
                """)
            .ToListAsync(ct);

        return
        [
            .. allApps.Where(a => AppTypeBehavior.IsRoutable(a.AppTypeId))
        ];
    }

    private sealed record ProxyAppLookup(Guid Id);
}
