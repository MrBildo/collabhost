namespace Collabhost.Api.Probes;

public class ProbeStartupService(ProbeService probeService, ILogger<ProbeStartupService> logger)
    : IHostedService
{
    private readonly ProbeService _probeService = probeService
        ?? throw new ArgumentNullException(nameof(probeService));

    private readonly ILogger<ProbeStartupService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running initial probe scan for all registered apps");

        try
        {
            await _probeService.RunProbesForAllAppsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to complete initial probe scan");
        }

        _logger.LogInformation("Initial probe scan complete");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
