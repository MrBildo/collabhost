namespace Collabhost.Api.Capabilities.Configurations;

public class AutoStartConfiguration
{
    public bool Enabled { get; set; }

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "enabled",
            "Auto-start on Platform Launch",
            FieldType.Boolean,
            new FieldEditableAlways()
        ),
    ];
}
