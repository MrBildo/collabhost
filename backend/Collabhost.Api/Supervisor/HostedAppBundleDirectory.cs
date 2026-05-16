namespace Collabhost.Api.Supervisor;

// Owns the per-app single-file bundle-extraction directory for hosted dotnet-apps.
//
// A default `dotnet publish` self-contained app must extract its embedded native
// libraries to a writable filesystem location on every cold start. Under the
// system-scope systemd unit (ProtectSystem=strict + ReadWritePaths scoped to the
// data/log/config dirs), the hosted app's working dir (operator-chosen, e.g.
// /srv/<slug>/) is read-only, so the host cannot self-extract there.
//
// Routing extraction under the data root -- which `install-system.sh` L60-62
// already grants for Collabhost's *own* binary -- means the existing
// ReadWritePaths=/var/lib/collabhost covers it with zero systemd-unit change.
// This type is the hosted-app analogue of that self-binary handling. The dir is
// per-slug so two hosted single-file apps never share an extraction subtree, and
// it is created lazily by the running service (owner-correct by construction --
// the install script does not know app slugs). (#313 / CH-C.)
public class HostedAppBundleDirectory
(
    string dataDirectory,
    ILogger<HostedAppBundleDirectory> logger
)
{
    public const string SubdirectoryName = "app-bundles";

    private readonly string _dataDirectory = !string.IsNullOrWhiteSpace(dataDirectory)
        ? dataDirectory
        : throw new ArgumentException("Data directory must be a non-empty path.", nameof(dataDirectory));
    private readonly ILogger<HostedAppBundleDirectory> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public string ResolveFor(string appSlug) => ResolvePath(_dataDirectory, appSlug);

    // Lazily creates the per-app bundle dir at app start. Best-effort: a failure
    // to create it is logged, not thrown -- the .NET single-file host falls back
    // to its own default ($HOME/.net) when the configured base dir is unusable,
    // so a hard throw here would regress an app that would otherwise have started
    // via the pre-#313 inherited-env path. Mirrors the existence-check posture
    // the supervisor already takes on the artifact location.
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
                "Failed to create per-app bundle directory '{Path}' for '{Slug}' -- the single-file host will fall back to its default extraction location",
                path,
                appSlug
            );
        }

        return path;
    }

    // Best-effort reap on app delete. Mirrors the install-time teardown property
    // L60-62 relies on (`rm -rf /var/lib/collabhost` reaps the parent); this
    // removes the per-app subtree at delete time so a deleted app does not leave
    // an extraction cache behind. Never throws -- delete is a cleanup courtesy.
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
                "Failed to reap per-app bundle directory '{Path}' for '{Slug}'",
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
