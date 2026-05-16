namespace Collabhost.Api.Supervisor;

// Owns the per-app sandbox-writable runtime working directory for hosted
// dotnet-apps. The structural twin of HostedAppBundleDirectory (#313 / CH-C),
// differing only in which child-process attribute it drives: CH-C grants a
// writable DOTNET_BUNDLE_EXTRACT_BASE_DIR; this grants a writable process
// working directory (cwd).
//
// A hosted dotnet-app whose default SQLite connection string is relative
// (Collaboard's is: Data Source=./data/collaboard.db) does
// Directory.CreateDirectory("./data/") at startup, resolved against the
// child's cwd -- which the supervisor sets to the artifact location. Under the
// system-scope systemd unit (ProtectSystem=strict + ReadWritePaths scoped to
// the data/log/config dirs), the operator-chosen artifact location (e.g.
// /srv/<slug>/) is read-only, so the relative ./data/ create fails with
// EROFS. (#316 -- the second, independent failure mode behind the v1.3.x
// hosted-Collaboard retry; Axis B.)
//
// Routing the runtime cwd under the data root -- which install-system.sh
// L60-62 already grants for Collabhost's own binary, already inside the
// unit's ReadWritePaths -- means the existing ReadWritePaths=/var/lib/collabhost
// covers it with zero systemd-unit change. The dir is per-slug so two hosted
// apps never share a cwd, and it is created lazily by the running service
// (owner-correct by construction -- the install script does not know app
// slugs).
public class HostedAppWorkingDirectory
(
    string dataDirectory,
    ILogger<HostedAppWorkingDirectory> logger
)
{
    public const string SubdirectoryName = "app-cwd";

    private readonly string _dataDirectory = !string.IsNullOrWhiteSpace(dataDirectory)
        ? dataDirectory
        : throw new ArgumentException("Data directory must be a non-empty path.", nameof(dataDirectory));
    private readonly ILogger<HostedAppWorkingDirectory> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public string ResolveFor(string appSlug) => ResolvePath(_dataDirectory, appSlug);

    // Lazily creates the per-app working dir at app start. Best-effort: a
    // failure to create it is logged, not thrown. The caller falls back to the
    // artifact location as cwd when this returns its path but the directory is
    // not actually usable -- a hard throw here would regress an app that would
    // otherwise have started. Mirrors HostedAppBundleDirectory's posture.
    public string EnsureFor(string appSlug)
    {
        var path = ResolvePath(_dataDirectory, appSlug);

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to create per-app working directory '{Path}' for '{Slug}' -- the hosted app will fall back to its artifact location as cwd",
                path,
                appSlug
            );
        }

        return path;
    }

    // Best-effort reap on app delete. Mirrors HostedAppBundleDirectory.Reap:
    // removes the per-app subtree at delete time so a deleted app does not
    // leave a state directory behind. Never throws -- delete is a cleanup
    // courtesy.
    public void Reap(string appSlug)
    {
        var path = ResolvePath(_dataDirectory, appSlug);

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to reap per-app working directory '{Path}' for '{Slug}'",
                path,
                appSlug
            );
        }
    }

    // Internal visibility for unit tests -- pure path composition, no I/O.
    internal static string ResolvePath(string dataDirectory, string appSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appSlug);

        return Path.Combine(dataDirectory, SubdirectoryName, appSlug);
    }
}
