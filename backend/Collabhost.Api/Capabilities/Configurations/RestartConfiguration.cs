using Collabhost.Api.Registry;

namespace Collabhost.Api.Capabilities.Configurations;

public class RestartConfiguration
{
    public RestartPolicy Policy { get; set; } = RestartPolicy.Never;

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
    ];

    private static string FormatPolicy(RestartPolicy value) => value switch
    {
        RestartPolicy.Never => "Never",
        RestartPolicy.OnCrash => "On Crash",
        RestartPolicy.Always => "Always",
        _ => value.ToString()
    };
}
