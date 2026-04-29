namespace Collabhost.Api.Proxy;

// Pure resolver implementing the Caddy binary precedence chain: env > config > bundled.
// Returns null when no Caddy binary can be located -- callers soft-fail with visibility.
//
// The operator contract is two-tier: the bundled sidecar (default) or COLLABHOST_CADDY_PATH
// (explicit absolute-path override). Proxy:BinaryPath in appsettings.json is honored as an
// undocumented escape hatch for absolute paths only -- bare-name PATH walking was removed
// in card #196.
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
                "{EnvVar} set to '{Path}' but file not found -- falling through to config / bundled",
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
                "Proxy:BinaryPath='{Path}' could not be resolved -- falling through to bundled",
                settings.BinaryPath
            );
        }

        // 3. Bundled sidecar next to the Collabhost binary (v1 default shipped in the archive).
        var bundledPath = Path.Combine
        (
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows() ? "caddy.exe" : "caddy"
        );

        if (File.Exists(bundledPath))
        {
            return Path.GetFullPath(bundledPath);
        }

        // 4. No Caddy binary available -- proxy subsystem disabled.
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
