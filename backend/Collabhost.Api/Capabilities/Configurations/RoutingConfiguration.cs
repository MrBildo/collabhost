using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities.Configurations;

public class RoutingConfiguration
{
    public string DomainPattern { get; set; } = "{slug}.{baseDomain}";

    public ServeMode ServeMode { get; set; } = ServeMode.ReverseProxy;

    public bool SpaFallback { get; set; }

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
    ];

    private static string FormatServeMode(ServeMode value) => value switch
    {
        ServeMode.ReverseProxy => "Reverse Proxy",
        ServeMode.FileServer => "File Server",
        _ => value.ToString()
    };
}
