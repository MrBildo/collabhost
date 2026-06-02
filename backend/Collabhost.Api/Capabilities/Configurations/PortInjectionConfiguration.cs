namespace Collabhost.Api.Capabilities.Configurations;

public class PortInjectionConfiguration
{
    public string EnvironmentVariableName { get; set; } = "PORT";

    public string PortFormat { get; set; } = "{port}";

    // A fixed listening port for the app. Zero (the default) means "no pin" --
    // the platform picks a free port automatically at each start, which is
    // correct for apps that nothing else addresses directly. Set a non-zero
    // value when another service reaches this app by its address
    // (localhost:<port>) and needs that address to stay the same across
    // restarts. A pinned port is held back from the automatic-allocation pool
    // so it can never be handed to a different app.
    public int FixedPort { get; set; }

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
        new
        (
            "fixedPort",
            "Fixed Port",
            FieldType.Number,
            new FieldEditableAlways(),
            RequiresRestart: true,
            HelpText: "Pin this app to a fixed listening port so its address stays "
                + "the same across restarts. Leave at 0 to let the platform pick a "
                + "free port automatically. Choose a value outside 32768-60999 (the "
                + "range used for automatic ports) to avoid clashing with other apps; "
                + "the pinned port is reserved so the platform never assigns it elsewhere.",
            MinValue: 0,
            MaxValue: 65535
        ),
    ];
}
