using System.Text.Json.Nodes;

using Collabhost.Api.Services.Proxy;

namespace Collabhost.Api.Tests.Fixtures;

public class FakeProxyConfigClient : IProxyConfigClient
{
    public JsonObject? LastPushedConfig { get; private set; }
    public int LoadCallCount { get; private set; }

    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<bool> LoadConfigAsync(JsonObject config, CancellationToken ct = default)
    {
        LastPushedConfig = config;
        LoadCallCount++;
        return Task.FromResult(true);
    }

    public Task<JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(LastPushedConfig);
}
