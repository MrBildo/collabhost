namespace Collabhost.Api.Capabilities;

// Per-binding schema overrides. AppType bindings may carry an optional schemaOverrides
// object mapping a field key to a property-override bag. Consumers project the FieldDescriptor
// schema through this map so different AppTypes that share a capability can ship type-specific
// copy (helpText, label, etc.) without leaking concerns into the shared schema.
//
// The seam is JSON-driven by-property: any property the binding declares an override for is
// honored. Today only helpText is exercised; the seam supports label, helpText, requiresRestart,
// unit, and required without further changes.
public static class SchemaOverrides
{
    private const string _schemaOverridesPropertyName = "schemaOverrides";

    // Extracts the schemaOverrides object from a binding's default JSON, returning a map
    // of fieldKey -> override JsonObject. Returns an empty map if the binding has no overrides
    // or if the JSON is malformed in any way -- callers fall through to base FieldDescriptor.
    public static IReadOnlyDictionary<string, JsonObject> Extract(string bindingJson)
    {
        if (string.IsNullOrWhiteSpace(bindingJson))
        {
            return FrozenDictionary<string, JsonObject>.Empty;
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(bindingJson);
        }
        catch (JsonException)
        {
            return FrozenDictionary<string, JsonObject>.Empty;
        }

        if (root is not JsonObject rootObject)
        {
            return FrozenDictionary<string, JsonObject>.Empty;
        }

        if (!rootObject.TryGetPropertyValue(_schemaOverridesPropertyName, out var overridesNode)
            || overridesNode is not JsonObject overridesObject)
        {
            return FrozenDictionary<string, JsonObject>.Empty;
        }

        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var (fieldKey, fieldOverride) in overridesObject)
        {
            if (fieldOverride is JsonObject fieldOverrideObject)
            {
                result[fieldKey] = fieldOverrideObject;
            }
        }

        return result;
    }

    // Projects a FieldDescriptor through the binding's per-field override JsonObject. Unspecified
    // properties fall through to the base descriptor. Pass null when the binding has no override
    // for this field -- the descriptor is returned unchanged.
    public static FieldDescriptor Apply(FieldDescriptor descriptor, JsonObject? fieldOverride) =>
        fieldOverride is null || fieldOverride.Count == 0
            ? descriptor
            : descriptor with
            {
                Label = ReadStringRequired(fieldOverride, "label", descriptor.Label),
                HelpText = ReadStringOptional(fieldOverride, "helpText", descriptor.HelpText),
                Unit = ReadStringOptional(fieldOverride, "unit", descriptor.Unit),
                RequiresRestart = ReadBool(fieldOverride, "requiresRestart", descriptor.RequiresRestart),
                Required = ReadBool(fieldOverride, "required", descriptor.Required)
            };

    private static string ReadStringRequired(JsonObject obj, string propertyName, string fallback) =>
        obj.TryGetPropertyValue(propertyName, out var node)
        && node is not null
        && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : fallback;

    private static string? ReadStringOptional(JsonObject obj, string propertyName, string? fallback) =>
        obj.TryGetPropertyValue(propertyName, out var node)
        && node is not null
        && node.GetValueKind() == JsonValueKind.String
            ? node.GetValue<string>()
            : fallback;

    private static bool ReadBool(JsonObject obj, string propertyName, bool fallback) =>
        obj.TryGetPropertyValue(propertyName, out var node)
        && node is not null
            ? node.GetValueKind() switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => fallback
            }
            : fallback;
}
