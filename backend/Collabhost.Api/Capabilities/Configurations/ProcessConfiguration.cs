using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities.Configurations;

public class ProcessConfiguration
{
    public DiscoveryStrategy DiscoveryStrategy { get; set; } = DiscoveryStrategy.Manual;

    public bool GracefulShutdown { get; set; }

    public int ShutdownTimeoutSeconds { get; set; } = 10;

    public string? Command { get; set; }

    public string? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "discoveryStrategy",
            "Discovery Strategy",
            FieldType.Select,
            new FieldEditableAlways(),
            Options: [.. Enum.GetValues<DiscoveryStrategy>()
                .Select(v => new FieldOption(v.ToString(), FormatDiscoveryStrategy(v)))]
        ),
        new
        (
            "command",
            "Command",
            FieldType.Text,
            new FieldEditableAlways(),
            DependsOn: new FieldDependency("discoveryStrategy", DiscoveryStrategy.Manual.ToString())
        ),
        new
        (
            "arguments",
            "Arguments",
            FieldType.Text,
            new FieldEditableAlways(),
            DependsOn: new FieldDependency("discoveryStrategy", DiscoveryStrategy.Manual.ToString())
        ),
        new
        (
            "workingDirectory",
            "Working Directory",
            FieldType.Text,
            new FieldEditableAlways()
        ),
        new
        (
            "gracefulShutdown",
            "Graceful Shutdown",
            FieldType.Boolean,
            new FieldEditableAlways()
        ),
        new
        (
            "shutdownTimeoutSeconds",
            "Shutdown Timeout",
            FieldType.Number,
            new FieldEditableAlways(),
            Unit: "sec"
        ),
    ];

    private static string FormatDiscoveryStrategy(DiscoveryStrategy value) => value switch
    {
        DiscoveryStrategy.DotNetRuntimeConfiguration => ".NET Runtime Config",
        DiscoveryStrategy.PackageJson => "Package JSON",
        DiscoveryStrategy.Manual => "Manual",
        _ => value.ToString()
    };
}
