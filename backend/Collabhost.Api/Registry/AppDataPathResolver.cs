namespace Collabhost.Api.Registry;

// Owns the per-app writable data path that Collabhost surfaces as a
// first-class API contract (registration response + get_app, REST and MCP --
// card #326 / #322 decision E1).
//
// The path is derived at runtime from the platform data root (the resolve-once
// effectiveDataDir from Program.cs, sourced from COLLABHOST_DATA_PATH first).
// That root already lives inside the system-scope systemd unit's
// ReadWritePaths -- the same root Collabhost's own DB and the single-file
// bundle dir use -- so an app told to write under this path works under
// ProtectSystem=strict with zero unit change. This is the standard model: the
// app is *told*, in configuration, exactly where to write, instead of guessing
// a cwd-relative path into a read-only tree.
//
// This is a pure path-composition contract (no I/O) and is intentionally NOT
// the now-removed Axis-B cwd-redirect accommodation (#322 decision D2). The
// subdirectory name is "app-data" -- the forward, operator-facing name for a
// writable data path the operator points app config at -- distinct from the
// internal "app-cwd" of the removed redirect. Nothing here is persisted; the
// value is recomputed per request from the same data root.
public class AppDataPathResolver(string dataDirectory)
{
    public const string SubdirectoryName = "app-data";

    private readonly string _dataDirectory = !string.IsNullOrWhiteSpace(dataDirectory)
        ? dataDirectory
        : throw new ArgumentException("Data directory must be a non-empty path.", nameof(dataDirectory));

    public string ResolveFor(string appSlug) => ResolvePath(_dataDirectory, appSlug);

    // Internal visibility for unit tests -- pure path composition, no I/O.
    internal static string ResolvePath(string dataDirectory, string appSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appSlug);

        return Path.GetFullPath(Path.Combine(dataDirectory, SubdirectoryName, appSlug));
    }
}
