using Collabhost.Api.Capabilities.Configurations;

namespace Collabhost.Api.Capabilities;

public static class CapabilityCatalog
{
    public static readonly FrozenDictionary<string, CapabilityDefinition> All =
        new Dictionary<string, CapabilityDefinition>(StringComparer.Ordinal)
        {
            ["process"] = new
            (
                "Process Management",
                typeof(ProcessConfiguration),
                ProcessConfiguration.Schema
            ),
            ["port-injection"] = new
            (
                "Port Injection",
                typeof(PortInjectionConfiguration),
                PortInjectionConfiguration.Schema
            ),
            ["routing"] = new
            (
                "Routing",
                typeof(RoutingConfiguration),
                RoutingConfiguration.Schema
            ),
            ["health-check"] = new
            (
                "Health Check",
                typeof(HealthCheckConfiguration),
                HealthCheckConfiguration.Schema
            ),
            ["environment-defaults"] = new
            (
                "Environment Variables",
                typeof(EnvironmentConfiguration),
                EnvironmentConfiguration.Schema
            ),
            ["restart"] = new
            (
                "Restart Policy",
                typeof(RestartConfiguration),
                RestartConfiguration.Schema
            ),
            ["auto-start"] = new
            (
                "Auto Start",
                typeof(AutoStartConfiguration),
                AutoStartConfiguration.Schema
            ),
            ["artifact"] = new
            (
                "Application Files",
                typeof(ArtifactConfiguration),
                ArtifactConfiguration.Schema
            ),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CapabilityDefinition? Get(string slug) =>
        All.GetValueOrDefault(slug);

    public static IReadOnlyList<FieldDescriptor>? GetSchema(string slug) =>
        All.TryGetValue(slug, out var definition) ? definition.Schema : null;

    public static bool IsKnown(string slug) =>
        All.ContainsKey(slug);
}

public record CapabilityDefinition
(
    string DisplayName,
    Type ConfigurationType,
    IReadOnlyList<FieldDescriptor> Schema
);
