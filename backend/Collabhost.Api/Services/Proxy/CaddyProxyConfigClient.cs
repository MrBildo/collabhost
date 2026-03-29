using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Collabhost.Api.Services.Proxy;

public class CaddyProxyConfigClient
(
    HttpClient httpClient,
    ProxySettings settings,
    ILogger<CaddyProxyConfigClient> logger
) : IProxyConfigClient
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<CaddyProxyConfigClient> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_settings.AdminApiUrl}/config/", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Proxy admin API not ready at {Url}", _settings.AdminApiUrl);
            return false;
        }
    }

    public async Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_settings.AdminApiUrl}/load", config, ct);

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
            var response = await _httpClient.GetAsync($"{_settings.AdminApiUrl}/config/", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning
                (
                    "Failed to get proxy config — status {StatusCode}",
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
