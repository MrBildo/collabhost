using System.Globalization;

using Collabhost.Api.Capabilities.Configurations;

namespace Collabhost.Api.HealthChecks;

// HTTP probe logic, separated from the hosted-service orchestration so it can be
// unit-tested with a fake HttpMessageHandler and exercised independently of the
// AppStore / CapabilityStore / Supervisor wiring.
public class HealthCheckProbe(HttpClient httpClient, TimeProvider timeProvider, ILogger<HealthCheckProbe> logger)
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<HealthCheckProbe> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<HealthCheckResult> ProbeAsync
    (
        string slug,
        string host,
        int port,
        string scheme,
        HealthCheckConfiguration configuration,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        var endpoint = string.IsNullOrWhiteSpace(configuration.Endpoint)
            ? "/health"
            : configuration.Endpoint;

        if (!endpoint.StartsWith('/'))
        {
            endpoint = "/" + endpoint;
        }

        var url = string.Format
        (
            CultureInfo.InvariantCulture,
            "{0}://{1}:{2}{3}",
            scheme,
            host,
            port,
            endpoint
        );

        var timeout = TimeSpan.FromSeconds(Math.Max(1, configuration.TimeoutSeconds));
        var sampledAt = _timeProvider.GetUtcNow().UtcDateTime;

        // Each probe gets its own linked cancellation source so a slow endpoint cannot
        // stall a host shutdown. The caller's token cancels every in-flight probe
        // immediately; CancelAfter cancels probes that outlive the configured timeout.
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        probeCts.CancelAfter(timeout);

        try
        {
            using var response = await _httpClient.GetAsync(url, probeCts.Token);

            return response.IsSuccessStatusCode
                ? new HealthCheckResult(HealthCheckStatus.Healthy, sampledAt, null)
                : new HealthCheckResult
                (
                    HealthCheckStatus.Unhealthy,
                    sampledAt,
                    "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)
                );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(HealthCheckStatus.Unhealthy, sampledAt, "timeout");
        }
        catch (HttpRequestException exception)
        {
            return new HealthCheckResult(HealthCheckStatus.Unhealthy, sampledAt, exception.Message);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            _logger.LogDebug(exception, "Health probe for '{Slug}' failed unexpectedly", slug);

            return new HealthCheckResult(HealthCheckStatus.Unhealthy, sampledAt, exception.Message);
        }
    }
}
