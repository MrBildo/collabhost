namespace Collabhost.Api.Proxy;

// LoadConfigAsync result. Carries the failure detail (error body + status) when Caddy
// rejects the config, so ProxyManager can surface a real cause string on /status's
// proxyDetail.lastSyncError field instead of a generic message. Card #217.
public record LoadConfigResult(bool Success, int? StatusCode, string? ErrorBody)
{
    public static LoadConfigResult Ok() =>
        new(true, null, null);

    public static LoadConfigResult Failed(int? statusCode, string? errorBody) =>
        new(false, statusCode, errorBody);
}

public interface ICaddyClient
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);

    Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default);

    Task<JsonObject?> GetConfigAsync(CancellationToken ct = default);
}

public class CaddyClient
(
    HttpClient httpClient,
    ILogger<CaddyClient> logger
) : ICaddyClient
{
    private readonly HttpClient _httpClient = httpClient
        ?? throw new ArgumentNullException(nameof(httpClient));

    private readonly ILogger<CaddyClient> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        // Narrow catches for readiness probe: only transport-level / timeout failures
        // are expected during Caddy warm-up. Any other exception (programmer error,
        // auth config error) must propagate so we don't silently hide real bugs
        // behind a "not ready" result. (Marcus O5, load-bearing for the post-launch readiness probe.)
        try
        {
            var response = await _httpClient.GetAsync(new Uri("config/", UriKind.Relative), ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Caddy admin API not reachable yet");

            return false;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Per-attempt timeout hit (not caller cancellation) -- treat as not-ready.
            _logger.LogDebug(ex, "Caddy admin API readiness probe timed out");

            return false;
        }
    }

    public async Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("load", config, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Proxy config loaded successfully");

                return LoadConfigResult.Ok();
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogWarning
            (
                "Proxy config load failed with status {StatusCode}: {Body}",
                (int)response.StatusCode,
                body
            );

            return LoadConfigResult.Failed((int)response.StatusCode, body);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to load proxy config -- transport error");

            return LoadConfigResult.Failed(null, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to load proxy config -- request timed out");

            return LoadConfigResult.Failed(null, "Request timed out contacting Caddy admin API");
        }
    }

    public async Task<JsonObject?> GetConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(new Uri("config/", UriKind.Relative), ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning
                (
                    "Failed to get proxy config -- status {StatusCode}",
                    (int)response.StatusCode
                );

                return null;
            }

            var node = await response.Content.ReadFromJsonAsync<JsonNode>(ct);

            return node as JsonObject;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get proxy config -- transport error");

            return null;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to get proxy config -- request timed out");

            return null;
        }
    }
}
