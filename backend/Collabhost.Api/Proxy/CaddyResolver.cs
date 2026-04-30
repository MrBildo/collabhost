namespace Collabhost.Api.Proxy;

// Pure resolver implementing the Caddy binary precedence chain: env > appsettings > null.
//
// Returns null when no Caddy binary is configured. Callers (ProxyManager) translate
// null into ProxyState.Disabled -- the proxy subsystem is a soft-fail by design.
//
// The "default" binary location is no longer hardcoded here. Install scripts
// (docs/install.ps1, docs/install.sh) seed Proxy:BinaryPath in appsettings.json on
// first install, pointing at the bundled caddy[.exe] next to the Collabhost binary.
// Operators who delete the entry, or who never ran an installer, get the honest
// "disabled" state until they configure something.
public static class CaddyResolver
{
    public const string EnvVarName = "COLLABHOST_CADDY_PATH";

    public static string? Resolve(ProxySettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        // 1. COLLABHOST_CADDY_PATH env var -- highest precedence among operator-visible sources.
        var envOverride = Environment.GetEnvironmentVariable(EnvVarName);

        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            if (File.Exists(envOverride))
            {
                return Path.GetFullPath(envOverride);
            }

            logger.LogWarning
            (
                "{EnvVar} set to '{Path}' but file not found -- falling through to appsettings",
                EnvVarName,
                envOverride
            );
        }

        // 2. Proxy:BinaryPath from appsettings.json. Treated as an absolute-or-relative path
        //    only; bare-name PATH lookups were removed in card #196.
        if (!string.IsNullOrWhiteSpace(settings.BinaryPath))
        {
            var resolvedFromSetting = ResolveBinaryPathSetting(settings.BinaryPath);

            if (resolvedFromSetting is not null)
            {
                return resolvedFromSetting;
            }

            logger.LogWarning
            (
                "Proxy:BinaryPath='{Path}' could not be resolved -- proxy subsystem will run disabled",
                settings.BinaryPath
            );
        }

        // 3. Nothing configured -- proxy subsystem disabled.
        return null;
    }

    // Exposed for tests. Returns the resolved absolute path, or null if the file does not
    // exist. Bare-name (PATH-walked) values are no longer supported -- a name without a
    // directory separator falls through to "not found" via File.Exists.
    internal static string? ResolveBinaryPathSetting(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        return File.Exists(binaryPath) ? Path.GetFullPath(binaryPath) : null;
    }
}
