using System.Net.Http.Json;

namespace Collabhost.Api.Proxy;

public interface ICaddyClient
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);

    Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default);

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
        try
        {
            var response = await _httpClient.GetAsync(new Uri("config/", UriKind.Relative), ct);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Caddy admin API not ready");

            return false;
        }
    }

    public async Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("load", config, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Proxy config loaded successfully");

                return true;
            }

            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogWarning
            (
                "Proxy config load failed with status {StatusCode}: {Body}",
                (int)response.StatusCode,
                body
            );

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load proxy config");

            return false;
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get proxy config");

            return null;
        }
    }
}
