namespace Collabhost.Api.Capabilities;

public static class CapabilityResolver
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static T Resolve<T>(string defaultConfigurationJson, string? overrideConfigurationJson)
        where T : class
    {
        var effectiveJson = overrideConfigurationJson is not null
            ? MergeJson(defaultConfigurationJson, overrideConfigurationJson)
            : defaultConfigurationJson;

        return JsonSerializer.Deserialize<T>(effectiveJson, _jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize {typeof(T).Name} from JSON.");
    }

    public static string ResolveJson(string defaultConfigurationJson, string? overrideConfigurationJson) =>
        overrideConfigurationJson is not null
            ? MergeJson(defaultConfigurationJson, overrideConfigurationJson)
            : defaultConfigurationJson;

    public static string MergeJson(string defaultsJson, string overridesJson)
    {
        var defaults = JsonNode.Parse(defaultsJson)?.AsObject()
            ?? throw new InvalidOperationException("Invalid defaults JSON.");

        var overrides = JsonNode.Parse(overridesJson)?.AsObject()
            ?? throw new InvalidOperationException("Invalid overrides JSON.");

        foreach (var property in overrides)
        {
            if (property.Value is JsonObject overrideDict
                && defaults[property.Key] is JsonObject defaultDict)
            {
                foreach (var entry in overrideDict)
                {
                    defaultDict[entry.Key] = entry.Value?.DeepClone();
                }
            }
            else
            {
                defaults[property.Key] = property.Value?.DeepClone();
            }
        }

        return defaults.ToJsonString(_jsonOptions);
    }

    public static IReadOnlyList<string> ValidateEdits
    (
        string capabilitySlug,
        JsonObject proposedOverrides,
        bool isNewApp
    )
    {
        var errors = new List<string>();

        var schema = CapabilityCatalog.GetSchema(capabilitySlug);

        if (schema is null)
        {
            return errors;
        }

        var knownKeys = new HashSet<string>
        (
            schema.Select(f => f.Key),
            StringComparer.Ordinal
        );

        foreach (var property in proposedOverrides)
        {
            if (!knownKeys.Contains(property.Key))
            {
                errors.Add($"{capabilitySlug}.{property.Key}: Unknown field.");
                continue;
            }

            var field = schema.Single(f => string.Equals(f.Key, property.Key, StringComparison.Ordinal));

            if (field.Editable is FieldEditableLocked locked)
            {
                errors.Add($"{capabilitySlug}.{property.Key}: {locked.Reason}");
            }
            else if (field.Editable is FieldEditableDerived derived && !isNewApp)
            {
                errors.Add($"{capabilitySlug}.{property.Key}: {derived.Reason}");
            }
        }

        return errors;
    }
}
