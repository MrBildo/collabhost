namespace Collabhost.Api.Capabilities.Configurations;

public class EnvironmentConfiguration
{
    public IDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "variables",
            "Environment Variables",
            FieldType.KeyValue,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Passed to the application's child process at startup. Use this for runtime secrets and configuration (e.g. API tokens, log levels)."
        ),
    ];
}
