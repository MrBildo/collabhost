using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.AppTypes;

internal static class CapabilityConfigurationValidator
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static string? Validate(string slug, JsonObject configJson)
    {
        var jsonString = configJson.ToJsonString();

        try
        {
            return slug switch
            {
                StringCatalog.Capabilities.Process => ValidateDeserialization<ProcessConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.PortInjection => ValidateDeserialization<PortInjectionConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.Routing => ValidateDeserialization<RoutingConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.HealthCheck => ValidateDeserialization<HealthCheckConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.EnvironmentDefaults => ValidateDeserialization<EnvironmentDefaultsConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.Restart => ValidateDeserialization<RestartConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.AutoStart => ValidateDeserialization<AutoStartConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.AspNetRuntime => ValidateDeserialization<AspNetRuntimeConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.NodeRuntime => ValidateDeserialization<NodeRuntimeConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.ReactRuntime => ValidateDeserialization<ReactRuntimeConfiguration>(jsonString, slug),
                StringCatalog.Capabilities.Artifact => ValidateDeserialization<ArtifactConfiguration>(jsonString, slug),
                _ => null // Unknown slugs are handled elsewhere
            };
        }
        catch (JsonException exception)
        {
            return $"Invalid configuration JSON for capability '{slug}': {exception.Message}";
        }
    }

    private static string? ValidateDeserialization<T>(string json, string slug) where T : class
    {
        var deserialized = JsonSerializer.Deserialize<T>(json, _jsonOptions);

        return deserialized is null
            ? $"Configuration for capability '{slug}' deserialized to null."
            : null;
    }
}
