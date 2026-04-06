namespace Collabhost.Api.Capabilities.Configurations;

public class ArtifactConfiguration
{
    public string Location { get; set; } = default!;

    public string? ProjectRoot { get; set; }

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "location",
            "Application Directory",
            FieldType.Directory,
            new FieldEditableLocked("Set during registration"),
            Required: true,
            RequiresRestart: true
        ),
        new
        (
            "projectRoot",
            "Project Root",
            FieldType.Directory,
            new FieldEditableAlways(),
            HelpText: "The project source directory (where package.json lives) when it differs from the application directory. Leave empty if they are the same."
        ),
    ];
}
