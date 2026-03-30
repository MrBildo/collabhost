using System.Text.Json.Nodes;

namespace Collabhost.Api.Services.Proxy;

public interface IProxyConfigClient
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);

    Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default);

    Task<JsonObject?> GetConfigAsync(CancellationToken ct = default);
}

public record AppRouteInfo
(
    string Slug,
    int? Port,
    string? InstallDirectory,
    string? ServeMode
);

public interface IProxyAppSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
