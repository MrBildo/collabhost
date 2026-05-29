namespace Collabhost.Api.Capabilities.Configurations;

// Managed runtime-config file for static-site apps. Card #336; write target
// corrected to the writable data dir in #369.
//
// Static-site apps have no process channel (the env-var delivery half doesn't
// exist for them), so the platform-managed equivalent for runtime configuration
// is a file the platform writes into the app's WRITABLE DATA DIRECTORY
// ({dataRoot}/app-data/{slug}/). The portal SPA fetches this file at runtime to
// learn its API base URL (and similar). The reverse proxy serves the writable
// copy via a path-scoped overlay root ahead of the artifact-rooted file_server
// (#369), so the served path is unchanged while the file lives in a granted-
// writable location that survives an artifact-dir redeploy.
//
// The capability composes with the overlay branch's `Cache-Control: no-cache`
// header (#369): every operator value change is reflected at the next portal
// fetch with no Caddy reload required.
//
// Empty-`Values` is the load-bearing migration safety: when `Values.Count == 0`
// the writer no-ops, preserving any operator-maintained file on disk. The
// moment the operator types a key-value pair the platform takes over (writes
// the full file, overwriting prior content). This is the deployment-takes-over-
// by-explicit-action shape required by CLAUDE.md Rule 3 (no silent overwrite,
// no silent deletion). It governs the writer's behavior toward an existing
// file -- it is not a serving-semantics rule (#369 Q1).
public class RuntimeConfigFileConfiguration
{
    // Relative path under the app's writable data directory (served at the same
    // path under the site via the overlay root -- #369); locked at registration
    // because path drift would leave the prior file on disk and start serving
    // from a new one. Validated server-side to reject absolute paths and
    // parent-directory traversal segments.
    public string Path { get; set; } = "/config.json";

    // Flat string->string map of JSON keys to JSON string values, serialized as
    // a single-level JSON object on disk. The capability schema is flat because
    // the existing CapabilityResolver.MergeJson is one-level-deep; nested values
    // would require a deeper merge with cross-capability blast radius (#336
    // design review, C-3).
    //
    // The explicit setter normalizes JSON null to an empty dictionary so an
    // override containing {"values":null} -- which passes ValidateEdits (null
    // is not a JsonObject) and flows through MergeJson's else-branch -- never
    // reaches the writer as a null reference. Without the guard the writer's
    // Values.Count check would NRE. Same shape as RoutingConfiguration.
    // ResponseHeaders (#308). #336 design review C-2.
    // IDE0032: cannot use auto-property -- the setter normalizes JSON null to
    // an empty dictionary, which an auto-property cannot express.
#pragma warning disable IDE0032
    private IDictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
#pragma warning restore IDE0032

    public IDictionary<string, string> Values
    {
        get => _values;
        set => _values = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static IReadOnlyList<FieldDescriptor> Schema =>
    [
        new
        (
            "path",
            "File Path",
            FieldType.Text,
            new FieldEditableLocked("Set during registration"),
            RequiresRestart: false,
            HelpText: "Relative path under the app's writable data directory where the file is written "
                + "(served at the same path under the site). Must not be absolute and must not contain "
                + "'..'. Default is \"/config.json\"."
        ),
        new
        (
            "values",
            "Values",
            FieldType.KeyValue,
            new FieldEditableAlways(),
            RequiresRestart: false,
            HelpText: "Key-value entries serialized as a flat JSON object and written to the configured "
                + "path inside the app's writable data directory. When empty, the platform leaves any "
                + "existing file on disk untouched (operator-maintained content is preserved). When "
                + "non-empty, the platform owns the file -- the next start or save renders this map as "
                + "the file contents.",
            KeyPattern: CapabilityResolver.RuntimeConfigFileKeyPatternString,
            KeyPatternMessage: CapabilityResolver.RuntimeConfigFileKeyPatternMessage
        ),
    ];
}
