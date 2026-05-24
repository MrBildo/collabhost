using System.Globalization;

using Collabhost.Api.Data;

namespace Collabhost.Api.Installation;

// CLI entry for `collabhost --update-hosts [--dry-run] [--hosts-path <path>]`. Sync-all by
// construction: reads the full AppStore slug set + Portal hostname and rewrites the marker
// block in /etc/hosts (Linux/macOS) or %SystemRoot%\System32\drivers\etc\hosts (Windows).
//
// Card #345. Dispatched from Program.cs BEFORE WebApplication.CreateBuilder, so the CLI does its
// own minimal IConfiguration build and resolves the data dir the same way the host's main path
// does -- mirroring the merge-appsettings CLI pattern.
public static class UpdateHostsCli
{
    public const int ExitOk = 0;
    public const int ExitUsage = 2;
    public const int ExitMissingHostsFile = 4;
    public const int ExitDataAccessFailed = 5;
    public const int ExitWriteFailed = 6;
    public const int ExitElevationRequired = 7;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (!TryParseArgs(args, out var dryRun, out var hostsPathOverride, out var argError))
        {
            await stderr.WriteLineAsync(argError);
            await stderr.WriteLineAsync("usage: collabhost --update-hosts [--dry-run] [--hosts-path <path>]");
            return ExitUsage;
        }

        var hostsPath = hostsPathOverride ?? HostsFilePath.Resolve();

        // Load minimal config the same way Program.cs does -- ContentRoot from env or
        // BaseDirectory, optional COLLABHOST_CONFIG_PATH json, env vars layered on top. This
        // mirrors Program.cs:45-75 so the CLI sees the same Portal:Subdomain / Proxy:BaseDomain /
        // ConnectionStrings:Host the host would.
        var contentRoot = Environment.GetEnvironmentVariable("ASPNETCORE_CONTENTROOT")
            ?? AppContext.BaseDirectory;

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(contentRoot)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true);

        var configPath = Environment.GetEnvironmentVariable("COLLABHOST_CONFIG_PATH");

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            configBuilder.AddJsonFile(configPath, optional: true);
        }

        configBuilder.AddEnvironmentVariables();

        var configuration = configBuilder.Build();

        var (_, resolvedDataDir) = DataRegistration.ResolveConnectionString(configuration, contentRoot);
        var effectiveDataDir = resolvedDataDir ?? Path.Combine(contentRoot, "data");

        HostsFileResolver.ResolvedHostnames resolved;

        try
        {
            resolved = await HostsFileResolver.ResolveAsync(configuration, effectiveDataDir, ct);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbUpdateException)
        {
            await stderr.WriteLineAsync
            (
                string.Format
                (
                    CultureInfo.InvariantCulture,
                    "update-hosts: failed to read app registry from {0}: {1}",
                    effectiveDataDir,
                    ex.Message
                )
            );

            await stderr.WriteLineAsync
            (
                "update-hosts: start Collabhost once so migrations run, then re-run this command."
            );

            return ExitDataAccessFailed;
        }

        var existingContent = File.Exists(hostsPath)
            ? await File.ReadAllTextAsync(hostsPath, ct)
            : string.Empty;

        // Detect line ending from the file's existing content; when the file is empty (or the
        // override points at a non-existent path under tests) fall back to platform-native.
        var lineEnding = HostsFileEditor.DetectLineEnding(existingContent);

        var blockBody = HostsFileEditor.ComposeBlockBody(resolved.Entries, lineEnding);

        if (dryRun)
        {
            await stdout.WriteLineAsync
            (
                string.Format
                (
                    CultureInfo.InvariantCulture,
                    "update-hosts (dry-run): would write {0} entries to {1}",
                    resolved.Entries.Count,
                    hostsPath
                )
            );

            await stdout.WriteLineAsync(HostsFileEditor.ComposeFullBlock(blockBody, lineEnding));

            foreach (var warning in resolved.CollisionWarnings)
            {
                await stderr.WriteLineAsync("update-hosts: " + warning);
            }

            return ExitOk;
        }

        if (!File.Exists(hostsPath))
        {
            await stderr.WriteLineAsync
            (
                string.Format
                (
                    CultureInfo.InvariantCulture,
                    "update-hosts: hosts file not found at {0}.",
                    hostsPath
                )
            );

            return ExitMissingHostsFile;
        }

        HostsFileEditor.RewriteResult rewriteResult;

        try
        {
            rewriteResult = HostsFileEditor.Rewrite(hostsPath, blockBody);
        }
        catch (UnauthorizedAccessException)
        {
            await PrintElevationMessageAsync(stderr, hostsPath);
            return ExitElevationRequired;
        }
        catch (IOException ex)
        {
            // Permission-denied on POSIX surfaces as IOException with errno mapped through (no
            // distinct UnauthorizedAccessException). Heuristic: if the file exists and the
            // process isn't elevated, treat as elevation-required; otherwise generic write
            // failure.
            if (IsPermissionDenied(ex))
            {
                await PrintElevationMessageAsync(stderr, hostsPath);
                return ExitElevationRequired;
            }

            await stderr.WriteLineAsync
            (
                string.Format
                (
                    CultureInfo.InvariantCulture,
                    "update-hosts: failed to write {0}: {1}",
                    hostsPath,
                    ex.Message
                )
            );

            return ExitWriteFailed;
        }

        await EmitOutcomeAsync(stdout, stderr, hostsPath, resolved, rewriteResult);

        return ExitOk;
    }

    private static bool TryParseArgs
    (
        string[] args,
        out bool dryRun,
        out string? hostsPathOverride,
        out string? error
    )
    {
        dryRun = false;
        hostsPathOverride = null;
        error = null;

        var index = 0;

        while (index < args.Length)
        {
            var arg = args[index];

            if (string.Equals(arg, "--dry-run", StringComparison.Ordinal))
            {
                dryRun = true;
                index++;
                continue;
            }

            if (string.Equals(arg, "--hosts-path", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    error = "update-hosts: --hosts-path requires a path argument";
                    return false;
                }

                hostsPathOverride = args[index + 1];
                index += 2;
                continue;
            }

            error = string.Format
            (
                CultureInfo.InvariantCulture,
                "update-hosts: unrecognized argument '{0}'",
                arg
            );

            return false;
        }

        return true;
    }

    private static async Task EmitOutcomeAsync
    (
        TextWriter stdout,
        TextWriter stderr,
        string hostsPath,
        HostsFileResolver.ResolvedHostnames resolved,
        HostsFileEditor.RewriteResult rewriteResult
    )
    {
        switch (rewriteResult.Outcome)
        {
            case HostsFileEditor.RewriteOutcome.NoChange:
                await stdout.WriteLineAsync
                (
                    string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "update-hosts: no changes ({0} entries already present in {1})",
                        resolved.Entries.Count,
                        hostsPath
                    )
                );
                break;

            case HostsFileEditor.RewriteOutcome.Replaced:
                await stdout.WriteLineAsync
                (
                    string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "update-hosts: updated {0} ({1} entries)",
                        hostsPath,
                        resolved.Entries.Count
                    )
                );
                break;

            case HostsFileEditor.RewriteOutcome.Appended:
                await stdout.WriteLineAsync
                (
                    string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "update-hosts: appended marker block to {0} ({1} entries)",
                        hostsPath,
                        resolved.Entries.Count
                    )
                );
                break;

            case HostsFileEditor.RewriteOutcome.AppendedWithOrphan:
                await stdout.WriteLineAsync
                (
                    string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "update-hosts: appended marker block to {0} ({1} entries)",
                        hostsPath,
                        resolved.Entries.Count
                    )
                );

                if (rewriteResult.Warning is not null)
                {
                    await stderr.WriteLineAsync("update-hosts: " + rewriteResult.Warning);
                }

                break;
        }

        foreach (var hostnameWarning in resolved.CollisionWarnings)
        {
            await stderr.WriteLineAsync("update-hosts: " + hostnameWarning);
        }
    }

    private static async Task PrintElevationMessageAsync(TextWriter stderr, string hostsPath)
    {
        await stderr.WriteLineAsync
        (
            string.Format
            (
                CultureInfo.InvariantCulture,
                "update-hosts: cannot write {0} -- permission denied.",
                hostsPath
            )
        );

        if (OperatingSystem.IsWindows())
        {
            await stderr.WriteLineAsync("update-hosts: re-run from an elevated PowerShell:");
            await stderr.WriteLineAsync("  Start-Process collabhost -ArgumentList '--update-hosts' -Verb RunAs");
        }
        else
        {
            await stderr.WriteLineAsync("update-hosts: re-run with elevation:");
            await stderr.WriteLineAsync("  sudo collabhost --update-hosts");
        }
    }

    private static bool IsPermissionDenied(IOException ex) =>
        ex.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("Access to the path", StringComparison.OrdinalIgnoreCase);
}

// Cross-platform hosts-file path resolver. Pure default; can be overridden via --hosts-path.
public static class HostsFilePath
{
    public static string Resolve() =>
        OperatingSystem.IsWindows()
            ? Path.Combine
            (
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers",
                "etc",
                "hosts"
            )
            : "/etc/hosts";
}
