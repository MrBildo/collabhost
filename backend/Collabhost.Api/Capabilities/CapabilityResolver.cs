using System.Globalization;

namespace Collabhost.Api.Capabilities;

public static partial class CapabilityResolver
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Server-side compile uses \z (not $) so a trailing newline does not pass
    // validation -- in .NET, $ matches before a final \n, which would admit a
    // newline-bearing key (e.g. "FOO\n") that lands downstream as a
    // newline-bearing environment variable name. \z is the strict
    // end-of-string anchor. (#310)
    //
    // The env-var pattern has no wire counterpart -- it is the default fallback
    // for KeyValue fields whose schema declares no KeyPattern, so it is not
    // surfaced to the frontend and the .NET-only \z is safe here.
    //
    // For the three patterns that DO ship to the frontend via FieldDescriptor.
    // KeyPattern (response-header / runtime-config-file / security-headers),
    // the wire-form const keeps the $ anchor (which JavaScript's ECMA-262 $
    // already treats as strict end-of-input -- the trailing-newline-admission
    // is a .NET-specific quirk) and the .NET-compiled regex uses \z. The
    // wire-to-compile mapping lives in ResolveKeyPattern.
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.None, 100)]
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
    // server-authoritative enforcement in ValidateEdits. The two forms differ
    // only in the end-anchor: $ on the wire (JavaScript-safe), \z in the
    // compiled regex (.NET strict; rejects trailing \n -- #310).
    public const string ResponseHeaderKeyPatternString =
        @"^/[^\s:]+::[!#$%&'*+.^_`|~0-9A-Za-z-]+$";

    public const string ResponseHeaderKeyPatternMessage =
        "Keys must be \"<path>::<HeaderName>\" -- a path starting with '/' "
        + "(no spaces or colons), '::', then a valid HTTP header name "
        + "(e.g. \"/config.json::Cache-Control\").";

    [GeneratedRegex(@"^/[^\s:]+::[!#$%&'*+.^_`|~0-9A-Za-z-]+\z", RegexOptions.None, 100)]
    private static partial Regex ResponseHeaderKeyPattern { get; }

    // JSON-key contract for the runtime-config-file capability (Card #336).
    // Keys land verbatim in a flat JSON object on disk, so the contract is
    // permissive -- any non-empty token without whitespace. This is intentionally
    // wider than the env-var contract (which is POSIX-identifier-strict) because
    // JSON identifiers commonly include hyphens (e.g. "api-base-url"), and wider
    // than the response-header contract (which carries a "<path>::<HeaderName>"
    // shape) because runtime-config keys have no such compound structure. The
    // bound on length is the regex engine's; the bound on key character set is
    // "no whitespace, not empty," which keeps the on-disk JSON deterministic.
    // Wire form uses $ (JavaScript-safe); compiled form uses \z (#310).
    public const string RuntimeConfigFileKeyPatternString = @"^[^\s]+$";

    public const string RuntimeConfigFileKeyPatternMessage =
        "Keys must be non-empty and contain no whitespace.";

    [GeneratedRegex(@"^[^\s]+\z", RegexOptions.None, 100)]
    private static partial Regex RuntimeConfigFileKeyPattern { get; }

    // HTTP-header-name contract for the security-headers capability (Card #309).
    // Mirrors the header-name half of ResponseHeaderKeyPatternString (RFC 7230
    // tchar set) -- security-headers carries blanket (no path) header rules,
    // so the compound "<path>::<HeaderName>" shape would be wrong. Without this
    // explicit declaration, ValidateEdits falls back to the env-var POSIX
    // pattern and rejects every legitimate HTTP header name (the env-var
    // pattern lacks '-'). Wire form uses $ (JavaScript-safe); compiled form
    // uses \z (#310).
    public const string SecurityHeaderKeyPatternString =
        @"^[!#$%&'*+.^_`|~0-9A-Za-z-]+$";

    public const string SecurityHeaderKeyPatternMessage =
        "Keys must be a valid HTTP header name "
        + "(e.g. \"X-Content-Type-Options\", \"Strict-Transport-Security\").";

    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+\z", RegexOptions.None, 100)]
    private static partial Regex SecurityHeaderKeyPattern { get; }

    // External-target host contract (Card #348). Permits loopback / RFC1918 /
    // link-local IPv4 / IPv6 loopback / *.local / *.lan -- the homelab /
    // container / Tailscale audience. ExternalTargetSettings.AllowPublicHosts
    // bypasses this pattern when an operator opts in to public-hostname
    // upstreams.
    //
    // Wire form uses $ (JavaScript-safe ECMA-262 anchor); compiled form uses
    // \z to refuse trailing \n -- a host bearing a newline is a config
    // landmine, not a legitimate hostname. The wire-vs-compile mapping lives
    // in ResolveValuePattern (mirrors the existing ResolveKeyPattern split
    // per #310).
    public const string ExternalTargetHostPatternString =
        @"^(localhost|127\.0\.0\.1|::1|"
        + @"10\.\d{1,3}\.\d{1,3}\.\d{1,3}|"
        + @"192\.168\.\d{1,3}\.\d{1,3}|"
        + @"172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|"
        + @"169\.254\.\d{1,3}\.\d{1,3}|"
        + @"[A-Za-z0-9-]+\.local|"
        + @"[A-Za-z0-9-]+\.lan)$";

    public const string ExternalTargetHostPatternMessage =
        "Host must be localhost, 127.0.0.1, ::1, an RFC1918 / link-local IPv4 "
        + "address, or a *.local / *.lan hostname. To front a public hostname, "
        + "set ExternalTarget:AllowPublicHosts = true in appsettings.";

    [GeneratedRegex
    (
        @"^(localhost|127\.0\.0\.1|::1|"
        + @"10\.\d{1,3}\.\d{1,3}\.\d{1,3}|"
        + @"192\.168\.\d{1,3}\.\d{1,3}|"
        + @"172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|"
        + @"169\.254\.\d{1,3}\.\d{1,3}|"
        + @"[A-Za-z0-9-]+\.local|"
        + @"[A-Za-z0-9-]+\.lan)\z",
        RegexOptions.None,
        100
    )]
    private static partial Regex ExternalTargetHostPattern { get; }

    // Permissive shape fallback when AllowPublicHosts is on. "host is a
    // non-empty string that looks like a hostname or IP" -- alphanumeric +
    // '.' + '-' + ':' (for IPv6 literals like ::1 or [fe80::1]). The policy
    // gate is upstream (AllowPublicHosts); this is the residual shape check.
    [GeneratedRegex(@"^[A-Za-z0-9.:\-]+\z", RegexOptions.None, 100)]
    private static partial Regex PermissiveHostnamePattern { get; }

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
            RuntimeConfigFileKeyPatternString => RuntimeConfigFileKeyPattern,
            SecurityHeaderKeyPatternString => SecurityHeaderKeyPattern,
            _ => new Regex(patternString, RegexOptions.None, TimeSpan.FromMilliseconds(100))
        };

    // Sibling of ResolveKeyPattern for ValuePattern (FieldDescriptor.ValuePattern,
    // Card #348). Same wire-vs-compile discipline (#310 LESSON) -- wire uses $,
    // compile uses \z. Returns null when the schema's pattern is recognized
    // AND the active policy explicitly bypasses it -- the external-target host
    // policy uses this to return null when AllowPublicHosts is on, falling
    // through to the permissive hostname fallback in the validator.
    //
    // The closed-set switch is the canonical safety net: ValuePattern values
    // are author-controlled (FieldDescriptor in *Configuration.Schema) and the
    // strict-anchor compiled form lives in a corresponding [GeneratedRegex]
    // partial property. The fallback compiles with a bounded timeout as
    // defense-in-depth for a future schema pattern that forgets to register
    // here -- it never sees untrusted input.
    public static Regex? ResolveValuePattern(string patternString, bool allowPublicHosts = false) =>
        patternString == ExternalTargetHostPatternString
            ? (allowPublicHosts ? null : ExternalTargetHostPattern)
            : new Regex(patternString, RegexOptions.None, TimeSpan.FromMilliseconds(100));

    public static IReadOnlyList<string> ValidateEdits
    (
        string capabilitySlug,
        JsonObject proposedOverrides,
        bool isNewApp,
        bool allowPublicHosts = false
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

            // Required-field check at registration time (isNewApp == true) for
            // any field whose schema declares Required. Surfaces explicitly-
            // empty values that the marshaller deserialized to default("") /
            // default(0) before they land in CapabilityStore. Existing
            // capabilities that already enforce required-ness via the
            // FieldEditableLocked guard ("Set during registration") still trip
            // that check; this is the parallel guard for fields whose Required
            // shape isn't covered by Locked semantics. Card #348.
            var fieldIsEmpty = IsEmptyValue(property.Value, field.Type);

            if (field.Required && isNewApp && fieldIsEmpty)
            {
                errors.Add($"{capabilitySlug}.{property.Key}: This field is required.");
            }

            // Numeric bounds (Number fields only). Out-of-range values are
            // rejected explicitly so the operator-facing message names the
            // bound instead of a downstream `dial = "<host>:-1"` symptom.
            // Card #348. Read the value via double-or-long fallback -- JSON
            // integers come back as long under System.Text.Json's lazy
            // value-shape inference, and TryGetValue<double>() does not
            // unbox a long-shaped JsonValue. Skip when the field is empty
            // (Required handles that case alone) so the operator sees one
            // error message per missing field, not two.
            if (field.Type == FieldType.Number
                && !fieldIsEmpty
                && property.Value is JsonValue numericValue
                && TryReadDouble(numericValue, out var asDouble))
            {
                if (field.MinValue is { } min && asDouble < min)
                {
                    errors.Add
                    (
                        $"{capabilitySlug}.{property.Key}: Must be greater than or equal to "
                        + min.ToString(CultureInfo.InvariantCulture) + "."
                    );
                }

                if (field.MaxValue is { } max && asDouble > max)
                {
                    errors.Add
                    (
                        $"{capabilitySlug}.{property.Key}: Must be less than or equal to "
                        + max.ToString(CultureInfo.InvariantCulture) + "."
                    );
                }
            }

            // Value-pattern check (Text fields). Same wire-vs-compile
            // discipline as KeyPattern -- the schema declares a wire form
            // (FE-consumable regex with $ anchor); ResolveValuePattern
            // returns the compile-side form (\z anchor) and may return null
            // when an active policy bypasses the check (e.g. external-target
            // host under AllowPublicHosts). When the pattern is bypassed AND
            // the field is the external-target host, fall through to a
            // permissive hostname-shape check so a value like "foo bar"
            // still gets rejected even when public hosts are allowed. The
            // policy gate is the *content* of host the operator may pick.
            // The residual shape check still applies. Card #348.
            if (field.Type == FieldType.Text
                && field.ValuePattern is { } valuePattern
                && property.Value is JsonValue stringValue
                && stringValue.TryGetValue<string>(out var asString)
                && !string.IsNullOrEmpty(asString))
            {
                // Empty string is handled by Required (when set) -- skip the
                // ValuePattern check here so an operator who left a required
                // field blank doesn't get TWO error messages for the same
                // condition. A non-Required field with an empty value
                // legitimately escapes the pattern check (operator hasn't
                // supplied a value yet).
                var compiled = ResolveValuePattern(valuePattern, allowPublicHosts);

                if (compiled is not null)
                {
                    if (!compiled.IsMatch(asString))
                    {
                        errors.Add
                        (
                            $"{capabilitySlug}.{property.Key}: "
                            + (field.ValuePatternMessage ?? "Value does not match the required pattern.")
                        );
                    }
                }
                else if (valuePattern == ExternalTargetHostPatternString
                    && !PermissiveHostnamePattern.IsMatch(asString))
                {
                    // AllowPublicHosts bypass active. Still enforce the
                    // permissive hostname shape so whitespace / illegal
                    // chars don't sneak through. Empty already handled by
                    // Required and the short-circuit above.
                    errors.Add
                    (
                        $"{capabilitySlug}.{property.Key}: "
                        + "Host must be a non-empty hostname or IP "
                        + "(letters, digits, '.', '-', ':' only)."
                    );
                }
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

        // Defense-in-depth: also run cross-field validation against the
        // in-flight delta so a single PUT carrying both EnableHsts:true and
        // Headers["Strict-Transport-Security"] surfaces the operator-facing
        // error message at the earliest possible point. The endpoint MUST
        // additionally call ValidateMergedOverrides against the post-merge
        // effective override -- the in-flight check alone is insufficient
        // because a two-step operator edit (save STS in headers, later toggle
        // EnableHsts) reaches the collision state with neither step rejected.
        // See ValidateMergedOverrides for the load-bearing check.
        ValidateCrossFieldEdits(capabilitySlug, proposedOverrides, errors);

        return errors;
    }

    // Helper. Does the proposed override value mean "operator left this
    // empty"? For Text and Directory the answer is null/missing or an empty
    // or whitespace-only string. For Number the answer is zero -- numbers
    // default to 0 on missing. For other types (Boolean, Select, KeyValue)
    // the answer is null/missing only -- "false", empty string, and empty
    // object are legitimate values, not absence.
    private static bool IsEmptyValue(JsonNode? value, FieldType type) =>
        value is null
            ? true
            : type switch
            {
                FieldType.Text or FieldType.Directory =>
                    value is JsonValue stringValue
                        && stringValue.TryGetValue<string>(out var s)
                        && string.IsNullOrWhiteSpace(s),
                FieldType.Number =>
                    value is JsonValue numericValue
                        && TryReadDouble(numericValue, out var d)
                        && Math.Abs(d) < double.Epsilon,
                _ => false
            };

    // System.Text.Json keeps numeric values in their original JSON shape
    // (long for integers, double for fractionals). TryGetValue<double> does
    // NOT unbox a long-shaped JsonValue and TryGetValue<long> does not
    // unbox a double-shaped JsonValue. Probe both shapes so the caller can
    // treat all numeric JSON as a double.
    private static bool TryReadDouble(JsonValue numericValue, out double value)
    {
        if (numericValue.TryGetValue<double>(out value))
        {
            return true;
        }

        if (numericValue.TryGetValue<long>(out var asLong))
        {
            value = asLong;
            return true;
        }

        if (numericValue.TryGetValue<int>(out var asInt))
        {
            value = asInt;
            return true;
        }

        value = 0;
        return false;
    }

    // Cross-field validation against the effective post-merge override
    // (existing-stored-override deep-merged with the in-flight delta).
    // Callers that hold only a partial delta -- the PUT settings endpoint is
    // the canonical case -- MUST invoke this after computing the merged
    // override; otherwise a two-step operator edit can reach a forbidden
    // cross-field state with neither step rejected by ValidateEdits alone.
    // The contract is a superset of ValidateEdits' in-flight cross-field
    // check; keeping both is defense-in-depth, not redundant work.
    public static IReadOnlyList<string> ValidateMergedOverrides
    (
        string capabilitySlug,
        JsonObject mergedOverride
    )
    {
        var errors = new List<string>();
        ValidateCrossFieldEdits(capabilitySlug, mergedOverride, errors);
        return errors;
    }

    // Cross-field validation: rules that depend on more than one field in the
    // same capability. Today this is HSTS convenience-vs-freeform collision on
    // security-headers; future cross-field rules belong here too. Operates on
    // whatever JsonObject is passed -- the in-flight delta (called from
    // ValidateEdits as defense-in-depth) or the post-merge effective override
    // (called from ValidateMergedOverrides as the load-bearing check).
    private static void ValidateCrossFieldEdits
    (
        string capabilitySlug,
        JsonObject overrides,
        List<string> errors
    )
    {
        if (!string.Equals(capabilitySlug, "security-headers", StringComparison.Ordinal))
        {
            return;
        }

        // The collision: EnableHsts: true AND Headers map carries a
        // Strict-Transport-Security entry. Both channels would compete for the
        // same emitted header; the operator must choose one.
        var enableHsts = overrides.TryGetPropertyValue("enableHsts", out var enableHstsNode)
            && enableHstsNode is JsonValue enableHstsValue
            && enableHstsValue.TryGetValue<bool>(out var enableHstsBool)
            && enableHstsBool;

        if (!enableHsts)
        {
            return;
        }

        if (overrides.TryGetPropertyValue("headers", out var headersNode)
            && headersNode is JsonObject headersObject)
        {
            foreach (var entry in headersObject)
            {
                if (string.Equals(entry.Key, "Strict-Transport-Security", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add
                    (
                        "security-headers: Cannot set 'Strict-Transport-Security' in headers map "
                        + "while Enable HSTS is on. Choose one channel -- either turn off Enable "
                        + "HSTS and author the header manually in the map, or leave the map entry "
                        + "out and let Enable HSTS govern the header."
                    );
                    break;
                }
            }
        }
    }
}
