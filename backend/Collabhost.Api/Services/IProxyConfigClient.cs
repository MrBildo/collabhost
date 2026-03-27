using System.Text.Json.Nodes;

namespace Collabhost.Api.Services;

public interface IProxyConfigClient
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);
    Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default);
    Task<JsonObject?> GetConfigAsync(CancellationToken ct = default);
}
