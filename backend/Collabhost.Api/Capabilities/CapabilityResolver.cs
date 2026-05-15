namespace Collabhost.Api.Capabilities;

public static partial class CapabilityResolver
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.None, 100)]
    private static partial Regex EnvironmentVariableKeyPattern { get; }

    // Flattened per-path response-header key contract for the static-site routing
    // capability (Card #308). Key shape is "<path>::<HeaderName>":
    //   - path: starts with '/', one-or-more non-whitespace non-colon chars
    //     (forbidding ':' in the path keeps the '::' separator unambiguous so
    //     the builder can split deterministically -- static file paths do not
    //     use ':' in practice).
    //   - "::" literal separator.
    //   - HeaderName: an RFC 7230 field-name token (1*tchar).
    // The string form is surfaced to the frontend via the settings-field DTO
    // (FieldDescriptor.KeyPattern); the compiled form below is the
    // server-authoritative enforcement in ValidateEdits.
    public const string ResponseHeaderKeyPatternString =
        @"^/[^\s:]+::[!#$%&'*+.^_`|~0-9A-Za-z-]+$";

    public const string ResponseHeaderKeyPatternMessage =
        "Keys must be \"<path>::<HeaderName>\" -- a path starting with '/' "
        + "(no spaces or colons), '::', then a valid HTTP header name "
        + "(e.g. \"/config.json::Cache-Control\").";

    [GeneratedRegex(ResponseHeaderKeyPatternString, RegexOptions.None, 100)]
    private static partial Regex ResponseHeaderKeyPattern { get; }

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

    // Replaces {slug} and {baseDomain} tokens in a domain pattern. Pass-through for custom patterns.
    public static string ResolveDomain(string domainPattern, string slug, string baseDomain) =>
        domainPattern
            .Replace("{slug}", slug, StringComparison.OrdinalIgnoreCase)
            .Replace("{baseDomain}", baseDomain, StringComparison.OrdinalIgnoreCase);

    // Maps a schema-declared key-pattern string to its precompiled regex.
    // KeyPattern is set only by trusted server-side schema code (FieldDescriptor
    // in *Configuration.Schema), never by operator input, so the canonical
    // patterns are a closed set. The fallback compiles with a bounded timeout
    // as defense-in-depth for a future schema pattern that forgets to register
    // here -- it never sees untrusted input.
    private static Regex ResolveKeyPattern(string patternString) =>
        patternString switch
        {
            ResponseHeaderKeyPatternString => ResponseHeaderKeyPattern,
            _ => new Regex(patternString, RegexOptions.None, TimeSpan.FromMilliseconds(100))
        };

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

            if (field.Editable is FieldEditableLocked locked && !isNewApp)
            {
                errors.Add($"{capabilitySlug}.{property.Key}: {locked.Reason}");
            }
            else if (field.Editable is FieldEditableDerived derived && !isNewApp)
            {
                errors.Add($"{capabilitySlug}.{property.Key}: {derived.Reason}");
            }

            // Validate key-value field keys against the field's declared key
            // pattern. Absent KeyPattern => the environment-variable contract
            // (valid POSIX identifiers) -- byte-identical to the prior
            // behavior, so existing env-var fields are unaffected. A field
            // that declares a KeyPattern (e.g. routing.responseHeaders) is
            // validated against that pattern with its own operator-facing
            // message. The DTO mirror of this rule is FieldDescriptor.KeyPattern.
            if (field.Type == FieldType.KeyValue && property.Value is JsonObject keyValueObject)
            {
                var keyPattern = field.KeyPattern is null
                    ? EnvironmentVariableKeyPattern
                    : ResolveKeyPattern(field.KeyPattern);

                var keyPatternMessage = field.KeyPattern is null
                    ? "Keys must start with a letter or underscore and contain only letters, digits, and underscores."
                    : field.KeyPatternMessage
                        ?? "Key does not match the required pattern.";

                foreach (var entry in keyValueObject)
                {
                    if (!keyPattern.IsMatch(entry.Key))
                    {
                        errors.Add
                        (
                            $"{capabilitySlug}.{property.Key}: Invalid key '{entry.Key}'. "
                            + keyPatternMessage
                        );
                    }
                }
            }
        }

        return errors;
    }
}
