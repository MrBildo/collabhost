using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Capabilities;

namespace Collabhost.Api.Features.Apps;

public record AppTypeReference
(
    string Id,
    string Name,
    string DisplayName
);

public record ProcessRuntimeState
(
    string State,
    int? Pid,
    double? UptimeSeconds,
    int RestartCount
);

public record RouteRuntimeState
(
    string State,
    string? Domain
);

public record RuntimeState
(
    ProcessRuntimeState? Process,
    RouteRuntimeState? Route
);

public record AppCapabilityResponse
(
    string Category,
    string DisplayName,
    JsonObject Resolved,
    JsonObject Defaults,
    bool HasOverrides
);

public record AppDetailResponse
(
    string Id,
    string Name,
    string DisplayName,
    AppTypeReference AppType,
    DateTime RegisteredAt,
    RuntimeState Runtime,
    Dictionary<string, AppCapabilityResponse> Capabilities
);

internal static class RuntimeStateBuilder
{
    internal static async Task<ProcessRuntimeState> BuildProcessStateAsync
    (
        ManagedProcess? managedProcess,
        IProcessStateNameResolver stateNameResolver,
        CancellationToken ct
    )
    {
        if (managedProcess is null)
        {
            var stoppedName = await stateNameResolver.ResolveDisplayNameAsync
            (
                Domain.Catalogs.IdentifierCatalog.ProcessStates.Stopped, ct
            );

            return new ProcessRuntimeState(stoppedName, null, null, 0);
        }

        var stateName = await stateNameResolver.ResolveDisplayNameAsync(managedProcess.ProcessStateId, ct);

        return new ProcessRuntimeState
        (
            stateName,
            managedProcess.Pid,
            managedProcess.UptimeSeconds,
            managedProcess.RestartCount
        );
    }

    internal static RouteRuntimeState? BuildRouteState
    (
        string appSlug,
        RoutingConfiguration? routingConfiguration,
        ProxyConfigManager proxyConfigManager
    )
    {
        if (routingConfiguration is null)
        {
            return null;
        }

        var domain = routingConfiguration.DomainPattern
            .Replace("{slug}", appSlug, StringComparison.OrdinalIgnoreCase);

        var isEnabled = proxyConfigManager.IsRouteEnabled(appSlug);
        var state = isEnabled ? "active" : "disabled";

        return new RouteRuntimeState(state, domain);
    }

    internal static Dictionary<string, AppCapabilityResponse> BuildCapabilityDictionary
    (
        IEnumerable<ResolvedCapabilityData> resolvedCapabilities
    )
    {
        var result = new Dictionary<string, AppCapabilityResponse>(StringComparer.Ordinal);

        foreach (var resolved in resolvedCapabilities)
        {
            var resolvedNode = JsonNode.Parse(resolved.ResolvedConfiguration)?.AsObject() ?? new JsonObject();
            var defaultsNode = JsonNode.Parse(resolved.DefaultConfiguration)?.AsObject() ?? new JsonObject();

            result[resolved.Slug] = new AppCapabilityResponse
            (
                resolved.Category,
                resolved.DisplayName,
                resolvedNode,
                defaultsNode,
                resolved.HasOverrides
            );
        }

        return result;
    }
}
