namespace Collabhost.Api.Services;

public class ProxyConfigManager
(
    IProxyConfigClient proxyClient,
    ProxyConfigGenerator generator,
    IServiceScopeFactory scopeFactory,
    ProxySettings settings,
    ILogger<ProxyConfigManager> logger
) : IHostedService
{
    private readonly IProxyConfigClient _proxyClient = proxyClient ?? throw new ArgumentNullException(nameof(proxyClient));
    private readonly ProxyConfigGenerator _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<ProxyConfigManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private CancellationTokenSource? _startupCancellation;
    private Task? _startupTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupTask = Task.Run(() => StartupPollAsync(_startupCancellation.Token), _startupCancellation.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_startupCancellation is not null)
        {
            await _startupCancellation.CancelAsync();
            _startupCancellation.Dispose();
        }

        if (_startupTask is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks — IHostedService pattern: StartAsync fires, StopAsync drains. No sync context in ASP.NET Core.
                await _startupTask;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
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

    private async Task StartupPollAsync(CancellationToken ct)
    {
        _logger.LogInformation("Proxy config manager waiting for proxy admin API at {Url}", _settings.AdminApiUrl);

        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (await _proxyClient.IsReadyAsync(ct))
            {
                _logger.LogInformation("Proxy admin API is ready — syncing routes");
                await SyncRoutesAsync(ct);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        if (!ct.IsCancellationRequested)
        {
            _logger.LogWarning
            (
                "Proxy admin API did not become ready within 30 seconds — skipping initial sync"
            );
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
}
