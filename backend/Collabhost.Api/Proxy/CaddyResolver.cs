using System.Diagnostics;

namespace Collabhost.Api.Proxy;

// Pure resolver implementing the Caddy binary precedence chain: env > config > bundled.
// Returns null when no Caddy binary can be located -- callers soft-fail with visibility.
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

        // 2. Proxy:BinaryPath from appsettings.json. Absolute path -> used as-is.
        //    Bare name -> PATH resolution via where/which.
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

    // Exposed for tests. Returns the resolved absolute path, or null if unresolvable.
    internal static string? ResolveBinaryPathSetting(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        // If the path contains a directory separator, treat as absolute/relative path.
        if (binaryPath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || binaryPath.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return File.Exists(binaryPath) ? Path.GetFullPath(binaryPath) : null;
        }

        // Bare name -- resolve via PATH using where/which.
        return ResolveFromPath(binaryPath);
    }

    // 2 second upper bound on PATH lookup. Normal completion is milliseconds; the timeout
    // defends against pathological environments (antivirus interposition, network drives on
    // %PATH%) hanging the API startup path, which calls Resolve during ProxyAppSeeder.SeedAsync.
    private static readonly TimeSpan _pathLookupTimeout = TimeSpan.FromSeconds(2);

    private static string? ResolveFromPath(string binaryName)
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";

        try
        {
            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = binaryName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();

            // WaitForExit(timeout) bounds the wall clock. Reading stdout after the child has
            // exited is safe. Calling ReadToEnd() before WaitForExit would block indefinitely
            // on a stuck child -- defeating the timeout entirely.
            if (!process.WaitForExit((int)_pathLookupTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception)
                {
                    // Best-effort kill. The process may have exited between WaitForExit returning
                    // false and our Kill call, or the OS may deny access.
                }

                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // 'where' on Windows may return multiple lines. Take the first.
                var firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];

                return firstLine;
            }
        }
        catch (Exception)
        {
            // Binary resolution failed -- caller treats as unresolvable.
        }

        return null;
    }
}
