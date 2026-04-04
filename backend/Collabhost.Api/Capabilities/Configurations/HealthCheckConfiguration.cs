namespace Collabhost.Api.Capabilities.Configurations;

public class HealthCheckConfiguration
{
    public string Endpoint { get; set; } = "/health";

    public int IntervalSeconds { get; set; } = 30;

    public int TimeoutSeconds { get; set; } = 5;

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "endpoint",
            "Health Endpoint",
            FieldType.Text,
            new FieldEditableAlways()
        ),
        new
        (
            "intervalSeconds",
            "Check Interval",
            FieldType.Number,
            new FieldEditableAlways(),
            Unit: "sec"
        ),
        new
        (
            "timeoutSeconds",
            "Timeout",
            FieldType.Number,
            new FieldEditableAlways(),
            Unit: "sec"
        ),
    ];
}
