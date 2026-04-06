namespace Collabhost.Api.Capabilities.Configurations;

public class PortInjectionConfiguration
{
    public string EnvironmentVariableName { get; set; } = "PORT";

    public string PortFormat { get; set; } = "{port}";

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "environmentVariableName",
            "Environment Variable",
            FieldType.Text,
            new FieldEditableAlways(),
            RequiresRestart: true
        ),
        new
        (
            "portFormat",
            "Port Format",
            FieldType.Text,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Use {port} as placeholder. Example: http://localhost:{port}"
        ),
    ];
}
