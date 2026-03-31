using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;

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
    internal static ProcessRuntimeState BuildProcessState(ManagedProcess? managedProcess)
    {
        if (managedProcess is null)
        {
            return new ProcessRuntimeState("stopped", null, null, 0);
        }

        var stateName = ResolveStateName(managedProcess.ProcessStateId);

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
            var configNode = JsonNode.Parse(resolved.ResolvedConfiguration)?.AsObject() ?? new JsonObject();

            result[resolved.Slug] = new AppCapabilityResponse
            (
                resolved.Category,
                resolved.DisplayName,
                configNode,
                resolved.HasOverrides
            );
        }

        return result;
    }

    private static string ResolveStateName(Guid stateId) => stateId switch
    {
        _ when stateId == IdentifierCatalog.ProcessStates.Stopped => StringCatalog.ProcessStates.Stopped.ToLowerInvariant(),
        _ when stateId == IdentifierCatalog.ProcessStates.Starting => StringCatalog.ProcessStates.Starting.ToLowerInvariant(),
        _ when stateId == IdentifierCatalog.ProcessStates.Running => StringCatalog.ProcessStates.Running.ToLowerInvariant(),
        _ when stateId == IdentifierCatalog.ProcessStates.Stopping => StringCatalog.ProcessStates.Stopping.ToLowerInvariant(),
        _ when stateId == IdentifierCatalog.ProcessStates.Crashed => StringCatalog.ProcessStates.Crashed.ToLowerInvariant(),
        _ when stateId == IdentifierCatalog.ProcessStates.Restarting => StringCatalog.ProcessStates.Restarting.ToLowerInvariant(),
        _ => "unknown"
    };
}

public sealed record ResolvedCapabilityData
(
    string Slug,
    string DisplayName,
    string Category,
    string ResolvedConfiguration,
    bool HasOverrides
);
