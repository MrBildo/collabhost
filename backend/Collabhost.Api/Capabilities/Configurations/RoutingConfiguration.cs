using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities.Configurations;

public class RoutingConfiguration
{
    public string DomainPattern { get; set; } = "{slug}.{baseDomain}";

    public ServeMode ServeMode { get; set; } = ServeMode.ReverseProxy;

    public bool SpaFallback { get; set; }

    // Flattened per-path response-header rules for file-server routes. Key is
    // "<path>::<HeaderName>", value is the header value (e.g.
    // "/config.json::Cache-Control" -> "no-cache"). Flattened (not nested) so
    // it reuses the existing string->string KeyValue field and keeps MergeJson
    // semantics identical to the env-var map. Card #308.
    public IDictionary<string, string> ResponseHeaders { get; set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "domainPattern",
            "Domain Pattern",
            FieldType.Text,
            new FieldEditableLocked("Set during registration"),
            RequiresRestart: true
        ),
        new
        (
            "serveMode",
            "Serve Mode",
            FieldType.Select,
            new FieldEditableLocked("Determined by app type"),
            RequiresRestart: true,
            Options: [.. Enum.GetValues<ServeMode>()
                .Select(v => new FieldOption(v.ToString(), FormatServeMode(v)))]
        ),
        new
        (
            "spaFallback",
            "SPA Fallback",
            FieldType.Boolean,
            new FieldEditableAlways(),
            RequiresRestart: true,
            DependsOn: new FieldDependency("serveMode", nameof(ServeMode.FileServer))
        ),
        new
        (
            "responseHeaders",
            "Response Headers",
            FieldType.KeyValue,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Per-path response headers for served files. Key is "
                + "\"<path>::<HeaderName>\", value is the header value "
                + "(e.g. \"/config.json::Cache-Control\" -> \"no-cache\"). "
                + "The path match is exact and scoped -- other files are "
                + "unaffected.",
            DependsOn: new FieldDependency("serveMode", nameof(ServeMode.FileServer)),
            KeyPattern: CapabilityResolver.ResponseHeaderKeyPatternString,
            KeyPatternMessage: CapabilityResolver.ResponseHeaderKeyPatternMessage
        ),
    ];

    private static string FormatServeMode(ServeMode value) => value switch
    {
        ServeMode.ReverseProxy => "Reverse Proxy",
        ServeMode.FileServer => "File Server",
        _ => value.ToString()
    };
}
