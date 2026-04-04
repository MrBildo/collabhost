namespace Collabhost.Api.Capabilities.Configurations;

public class ArtifactConfiguration
{
    public string Location { get; set; } = default!;

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "location",
            "Application Directory",
            FieldType.Directory,
            new FieldEditableAlways(),
            Required: true
        ),
    ];
}
