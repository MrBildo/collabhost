using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities.Configurations;

public class RestartConfiguration
{
    public RestartPolicy Policy { get; set; } = RestartPolicy.Never;

    public int[] SuccessExitCodes { get; set; } = [0];

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "policy",
            "Restart Policy",
            FieldType.Select,
            new FieldEditableAlways(),
            Options: [.. Enum.GetValues<RestartPolicy>()
                .Select(v => new FieldOption(v.ToString(), FormatPolicy(v)))]
        ),
        new
        (
            "successExitCodes",
            "Success Exit Codes",
            FieldType.Text,
            new FieldEditableAlways(),
            HelpText: "Comma-separated exit codes that indicate a clean shutdown (e.g. 0,130)"
        ),
    ];

    private static string FormatPolicy(RestartPolicy value) => value switch
    {
        RestartPolicy.Never => "Never",
        RestartPolicy.OnCrash => "On Crash",
        RestartPolicy.Always => "Always",
        _ => value.ToString()
    };
}
