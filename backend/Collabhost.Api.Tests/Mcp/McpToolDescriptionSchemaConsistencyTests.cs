using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Mcp;
using Collabhost.Api.Registry;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Mcp;

// Card #306 -- structural regression guard for MCP tool description-vs-schema drift.
//
// Failure shape this catches (CH-5, the input-shape drift class, fixed in v1.3.0 / f25f87b):
// a tool's [Description] text claims a key/value/slug that the enforced input validator
// rejects. Concrete example: register_app's description used to list `executablePath` as a
// valid process key. ProcessConfiguration.Schema has no such key, so an agent following the
// description got an "Unknown field." rejection from CapabilityResolver.ValidateEdits.
//
// Scope honesty: this guards the CH-5 input-shape class only. The CH-6 sibling class --
// return-shape drift, where a tool's description claims a return field that the actual
// return literal does not produce (e.g. list_apps's "status, type, and route URL" prose,
// register_app's "writableDataPath" claim, get_app's enumerated return fields) -- is NOT
// covered by these 5 checks. The mechanic differs: no validator enforces return shape, so
// the cross-check target would be the C# return literal itself, not a catalog/enum/schema.
// That is a separate piece of work with its own cost tier, deliberately out of scope here.
//
// Why structural and not snapshot:
//
// - A snapshot test (compare description text to a saved string) fails on every legitimate
//   wording tweak and trains the team to update the snapshot blindly -- low signal.
// - A Roslyn analyzer for an 18-tool surface is overkill -- one test class is cheaper and
//   keeps the rule next to the code it protects.
// - Structural extraction (parse the JSON examples and "Valid <thing>:" prose, then check
//   each token against the live catalog/enum/schema) gives the lowest noise and highest
//   signal: it only fires when a description claims an input shape the enforcement
//   actually rejects -- which is the CH-5 class precisely.
//
// What it checks:
//
// 1. Every embedded JSON example -- e.g. `{"process":{"command":"./myapp",...}}` -- parses
//    cleanly. Each top-level key (section) must be a known capability slug per
//    CapabilityCatalog. Each property inside a section must appear in that capability's
//    FieldDescriptor schema.
// 2. Every "Valid <capability> keys: a, b, c." enumeration must be a subset of the
//    capability's schema keys.
// 3. Every "Valid values: ..." list must be a subset of the ProcessState.ToApiString()
//    result set. (Today the only `Valid values:` enumeration is list_apps's status filter.)
// 4. Every quoted app-type slug in a parameter that introduces app-type identifiers must
//    resolve via TypeStore (covers built-ins; user types are loaded at runtime and would
//    be present in the fixture's seeded TypeStore too).
//
// Adding a new tool description that mentions a new capability key is fine -- the test
// only enforces consistency, not closed-world membership. Adding a typo or a removed key
// fails the test at PR time.
[Collection("Api")]
public class McpToolDescriptionSchemaConsistencyTests(ApiFixture fixture)
{
    private static readonly Type[] _toolTypes =
    [
        typeof(DiscoveryTools),
        typeof(LifecycleTools),
        typeof(ConfigurationTools),
        typeof(RegistrationTools),
        typeof(ActivityLogTools),
    ];

    // Tokens that look like app-type slugs but legitimately appear in app-type-context prose
    // (parameter names, example values that are not app types, etc.). Add only with a comment
    // explaining why the token is not an app-type claim.
    private static readonly HashSet<string> _appTypeContextSkipList = new(StringComparer.Ordinal)
    {
        // Example app names used in parameter descriptions, e.g. "my-api-server".
        "my-api-server",
        "my-api",
    };

    private readonly TypeStore _typeStore = fixture.Services.GetRequiredService<TypeStore>();

    [Fact]
    public void EveryTool_HasDescription_AndNonEmptyName()
    {
        var methods = EnumerateToolMethods().ToList();

        methods.ShouldNotBeEmpty("Reflection found no [McpServerTool] methods -- test setup is wrong.");

        foreach (var entry in methods)
        {
            entry.ToolName.ShouldNotBeNullOrWhiteSpace($"{entry.MethodLabel}: [McpServerTool] Name is empty.");
            entry.Description.ShouldNotBeNullOrWhiteSpace($"{entry.ToolName} ({entry.MethodLabel}): [Description] is empty.");
        }
    }

    [Fact]
    public void JsonExamplesInDescriptions_AreParseable_AndReferenceKnownCapabilitiesAndKeys()
    {
        var validCapabilitySlugs = CapabilityCatalog.All.Keys.ToHashSet(StringComparer.Ordinal);

        // JSON examples appear in BOTH tool-level and parameter-level descriptions
        // (e.g. register_app's `settings` parameter and update_settings' `settings` parameter
        // both carry capability-shaped JSON example bodies). Scan both.
        foreach (var entry in EnumerateAllDescriptions())
        {
            foreach (var jsonLiteral in ExtractJsonObjectLiterals(entry.Description))
            {
                JsonObject? parsed;

                try
                {
                    parsed = JsonNode.Parse(jsonLiteral)?.AsObject();
                }
                catch (JsonException ex)
                {
                    parsed = null;
                    parsed.ShouldNotBeNull
                    (
                        $"{entry.ToolName} ({entry.MethodLabel}): description embeds JSON that fails to parse "
                        + $"({ex.Message}). Fix the example or remove it.\nLiteral: {jsonLiteral}"
                    );
                }

                if (parsed is null)
                {
                    continue;
                }

                foreach (var (sectionKey, sectionValue) in parsed)
                {
                    validCapabilitySlugs.ShouldContain
                    (
                        sectionKey,
                        $"{entry.ToolName} ({entry.MethodLabel}): JSON example references unknown capability "
                        + $"section '{sectionKey}'. Known: {string.Join(", ", validCapabilitySlugs.Order(StringComparer.Ordinal))}."
                    );

                    if (sectionValue is not JsonObject sectionObject)
                    {
                        continue;
                    }

                    var schemaKeys = CapabilityCatalog.GetSchema(sectionKey)?
                        .Select(f => f.Key)
                        .ToHashSet(StringComparer.Ordinal)
                        ?? [];

                    foreach (var (fieldKey, _) in sectionObject)
                    {
                        schemaKeys.ShouldContain
                        (
                            fieldKey,
                            $"{entry.ToolName} ({entry.MethodLabel}): JSON example claims '{sectionKey}.{fieldKey}', "
                            + $"but {sectionKey} schema has no such field. "
                            + $"Valid keys: {string.Join(", ", schemaKeys.Order(StringComparer.Ordinal))}. "
                            + "(This is the CH-5 / #306 class -- description claims a key the validator rejects.)"
                        );
                    }
                }
            }
        }
    }

    [Fact]
    public void ValidKeysEnumerations_AreSubsetOfCapabilitySchema()
    {
        // Matches phrasing like:  "Valid process keys: command, arguments, ..."
        // The capability slug between "Valid" and "keys" is the anchor; the comma list is the claim.
        var pattern = new Regex
        (
            @"Valid\s+(?<cap>[a-z][a-z0-9-]*)\s+keys?:\s*(?<list>[^.]+)\.",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(200)
        );

        foreach (var entry in EnumerateAllDescriptions())
        {
            foreach (Match match in pattern.Matches(entry.Description))
            {
                var capabilitySlug = match.Groups["cap"].Value;

                var schema = CapabilityCatalog.GetSchema(capabilitySlug);

                schema.ShouldNotBeNull
                (
                    $"{entry.ToolName} ({entry.MethodLabel}): 'Valid {capabilitySlug} keys:' refers to a capability "
                    + "that is not in CapabilityCatalog."
                );

                var schemaKeys = schema.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);

                var claimedKeys = match.Groups["list"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var claimed in claimedKeys)
                {
                    schemaKeys.ShouldContain
                    (
                        claimed,
                        $"{entry.ToolName} ({entry.MethodLabel}): 'Valid {capabilitySlug} keys' lists '{claimed}', "
                        + $"but {capabilitySlug} schema has no such field. "
                        + $"Valid keys: {string.Join(", ", schemaKeys.Order(StringComparer.Ordinal))}."
                    );
                }
            }
        }
    }

    [Fact]
    public void StatusFilterEnumeration_IsSubsetOfProcessStateApiStrings()
    {
        // Matches the canonical enumeration shape:
        //   "Valid values: running, stopped, crashed, backoff, fatal."
        // Today `Valid values:` only appears on the list_apps status filter, so anchoring on
        // it alone is unambiguous. An earlier draft prefix-gated on the literal "status"
        // before "Valid values:" with `status[^.]*?Valid\s+values:` -- but the description
        // reads "Filter by app status. Valid values: ...", and the lazy non-period quantifier
        // [^.]*? cannot cross the period between "status" and "Valid values:". That made the
        // pattern match zero descriptions in production -- the test passed only because no
        // matches meant no assertions. Dropping the prefix gate fixes the dead check and
        // makes the test fire on any future `Valid values:` enumeration regardless of
        // surrounding prose -- a stricter, more honest guard. Process-state semantics are
        // enforced via the subset check against ProcessState.ToApiString().
        var pattern = new Regex
        (
            @"Valid\s+values:\s*(?<list>[^.]+)\.",
            RegexOptions.IgnoreCase | RegexOptions.Singleline,
            TimeSpan.FromMilliseconds(200)
        );

        var validStatuses = Enum.GetValues<ProcessState>()
            .Select(s => s.ToApiString())
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in EnumerateAllDescriptions())
        {
            foreach (Match match in pattern.Matches(entry.Description))
            {
                var claimedStatuses = match.Groups["list"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var claimed in claimedStatuses)
                {
                    validStatuses.ShouldContain
                    (
                        claimed,
                        $"{entry.ToolName} ({entry.MethodLabel}): status-filter description lists '{claimed}', "
                        + $"but ProcessState.ToApiString() never produces it. "
                        + $"Valid statuses: {string.Join(", ", validStatuses.Order(StringComparer.Ordinal))}."
                    );
                }
            }
        }
    }

    [Fact]
    public void QuotedAppTypeSlugs_InEgEnumerations_AreKnownToTypeStore()
    {
        // App-type slugs appear in single quotes in `e.g.,` enumerations, e.g.
        //   "App type slug from list_app_types (e.g., 'dotnet-app', 'nodejs-app', ...)."
        // Narrowing to the `e.g.,` parenthetical avoids false positives from quoted status
        // strings or example app names elsewhere in the prose (e.g. "'stopped' status",
        // "'my-api-server'"). Drift in the canonical enumeration is the actual class we
        // want to catch.
        var egEnumeration = new Regex
        (
            @"e\.g\.,?\s*(?<list>(?:'[a-z][a-z0-9-]*'(?:\s*,\s*)?)+)",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(200)
        );

        var slugPattern = new Regex
        (
            @"'(?<slug>[a-z][a-z0-9-]*)'",
            RegexOptions.None,
            TimeSpan.FromMilliseconds(200)
        );

        var isAppTypeContext = new Regex
        (
            @"app\s*type|list_app_types",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(200)
        );

        var knownSlugs = _typeStore.ListTypes()
            .Select(t => t.Slug)
            .ToHashSet(StringComparer.Ordinal);

        knownSlugs.ShouldNotBeEmpty("TypeStore.ListTypes() returned no types -- fixture seeding is broken.");

        foreach (var entry in EnumerateAllDescriptions())
        {
            if (!isAppTypeContext.IsMatch(entry.Description))
            {
                continue;
            }

            foreach (Match enumerationMatch in egEnumeration.Matches(entry.Description))
            {
                var list = enumerationMatch.Groups["list"].Value;

                foreach (Match slugMatch in slugPattern.Matches(list))
                {
                    var slug = slugMatch.Groups["slug"].Value;

                    // A known capability slug appearing in app-type-context prose is not an
                    // app-type claim, so skip it.
                    if (CapabilityCatalog.IsKnown(slug))
                    {
                        continue;
                    }

                    if (_appTypeContextSkipList.Contains(slug))
                    {
                        continue;
                    }

                    knownSlugs.ShouldContain
                    (
                        slug,
                        $"{entry.ToolName} ({entry.MethodLabel}): description's e.g.-enumeration "
                        + $"includes '{slug}' in an app-type context, but TypeStore has no such type. "
                        + $"Known: {string.Join(", ", knownSlugs.Order(StringComparer.Ordinal))}. "
                        + $"(If '{slug}' is not actually an app-type slug, add it to "
                        + "_appTypeContextSkipList in this test file.)"
                    );
                }
            }
        }
    }

    private IEnumerable<DescriptionEntry> EnumerateToolMethods()
    {
        foreach (var toolType in _toolTypes)
        {
            foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();

                if (toolAttr is null)
                {
                    continue;
                }

                var descAttr = method.GetCustomAttribute<DescriptionAttribute>();
                var description = descAttr?.Description ?? string.Empty;
                var label = $"{toolType.Name}.{method.Name}";

                yield return new DescriptionEntry(toolAttr.Name ?? string.Empty, method, description, label);
            }
        }
    }

    // Like EnumerateToolMethods, but also yields each parameter's [Description] -- because
    // app-type slug claims live on parameter descriptions, not tool-level descriptions.
    private IEnumerable<DescriptionEntry> EnumerateAllDescriptions()
    {
        foreach (var entry in EnumerateToolMethods())
        {
            yield return entry;

            foreach (var parameter in entry.Method.GetParameters())
            {
                var paramDesc = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;

                if (string.IsNullOrEmpty(paramDesc))
                {
                    continue;
                }

                yield return new DescriptionEntry
                (
                    entry.ToolName,
                    entry.Method,
                    paramDesc,
                    $"{entry.MethodLabel}({parameter.Name})"
                );
            }
        }
    }

    // Extract balanced `{...}` substrings that look like JSON object literals embedded in
    // prose. The descriptions don't nest unbalanced braces, so a depth-counting scan is
    // sufficient. We tolerate quoted strings (so `"foo}"` inside a JSON string doesn't break
    // the balance count). We require the brace to be followed (after optional whitespace) by
    // a `"` so that `{slug}`-style placeholder tokens (which are not JSON examples) are
    // skipped.
    private static IEnumerable<string> ExtractJsonObjectLiterals(string text)
    {
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] != '{' || !LooksLikeJsonObjectStart(text, index))
            {
                index++;
                continue;
            }

            var literal = TryReadBalancedObject(text, index, out var consumed);

            if (literal is not null)
            {
                yield return literal;
            }

            // Always advance by at least one to ensure progress, even if the brace was unbalanced.
            index += Math.Max(1, consumed);
        }
    }

    private static bool LooksLikeJsonObjectStart(string text, int braceIndex)
    {
        for (var i = braceIndex + 1; i < text.Length; i++)
        {
            var ch = text[i];

            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            // A real JSON object opens with `"` (a key) or is empty `{}`.
            return ch is '"' or '}';
        }

        return false;
    }

    private static string? TryReadBalancedObject(string text, int start, out int consumed)
    {
        var depth = 0;
        var inString = false;
        var escape = false;

        for (var cursor = start; cursor < text.Length; cursor++)
        {
            var ch = text[cursor];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (ch == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;

                if (depth == 0)
                {
                    consumed = cursor - start + 1;
                    return text.Substring(start, consumed);
                }
            }
        }

        // Unbalanced -- treat as not-a-JSON-object so the outer scan can keep moving.
        consumed = 1;
        return null;
    }

    private sealed record DescriptionEntry(string ToolName, MethodInfo Method, string Description, string MethodLabel);
}
