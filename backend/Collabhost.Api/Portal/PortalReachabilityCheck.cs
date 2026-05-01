namespace Collabhost.Api.Portal;

// Boot-time soft preflight for the Portal's static-asset bundle. Mirrors the posture of
// ListenPortValidator: surface misconfiguration as a structured warning rather than halting
// boot. Two legitimate "missing" states (packaging regression, intentional stripped
// deployment) make halt-on-missing the wrong call -- a missing dashboard is degraded mode,
// halting trades it for fully unreachable.
public static class PortalReachabilityCheck
{
    public static PortalReachabilityOutcome Validate(string baseDirectory, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var wwwrootPath = Path.Combine(baseDirectory, "wwwroot");
        var indexHtmlPath = Path.Combine(wwwrootPath, "index.html");
        var assetsPath = Path.Combine(wwwrootPath, "assets");

        return (!Directory.Exists(wwwrootPath) || !File.Exists(indexHtmlPath))
            ? new PortalReachabilityOutcome
            (
                PortalReachabilityStatus.Missing,
                wwwrootPath,
                [
                    "Re-run the installer to restore the Portal.",
                    "If running in dev, expect the Portal at the Vite dev server (http://localhost:5173)."
                ]
            )
            : (!Directory.Exists(assetsPath) || !DirectoryHasAnyEntry(assetsPath))
                ? new PortalReachabilityOutcome
                (
                    PortalReachabilityStatus.AssetsEmpty,
                    wwwrootPath,
                    [
                        "Re-run the installer with the latest archive.",
                        "If you customized wwwroot/, restore it from the archive's wwwroot/assets/ directory."
                    ]
                )
                : new PortalReachabilityOutcome(PortalReachabilityStatus.Ok, wwwrootPath, []);
    }

    private static bool DirectoryHasAnyEntry(string path)
    {
        // EnumerateFileSystemEntries avoids materializing the whole listing; we only need
        // to know whether the directory has at least one child.
        using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        return enumerator.MoveNext();
    }
}
