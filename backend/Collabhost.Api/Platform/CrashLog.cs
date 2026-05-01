using System.Globalization;

namespace Collabhost.Api.Platform;

// Crash log writer for post-mortem diagnostics. When the host fails to start (preflight,
// migration, type store, seeder) or an unhandled exception escapes the runtime, we write
// a timestamped file under {LogsPath} so the operator has on-disk evidence after the
// process is gone. Stdout/stderr are gone with the terminated process; this file is the
// post-mortem surface.
//
// Resolution order for {LogsPath}:
//   1. COLLABHOST_LOGS_PATH env var (operator override).
//   2. Diagnostics:CrashLogs:Directory appsetting.
//   3. {DataDirectory}/logs/  (default).
//
// Retention: keep-last-N (default 10), oldest pruned by file write time. Single
// process, single writer -- no locking needed beyond what File.Create provides.
//
// Intentionally separate from OpenTelemetry. OTel may not have flushed at the moment
// we write, and the operator's debug surface should not depend on a collector being
// reachable. The crash log file is always local, always immediate.
public static class CrashLog
{
    public const string DefaultDirectoryName = "logs";
    public const string EnvironmentVariableName = "COLLABHOST_LOGS_PATH";
    public const string ConfigurationDirectoryKey = "Diagnostics:CrashLogs:Directory";
    public const string ConfigurationRetentionKey = "Diagnostics:CrashLogs:Retention";
    public const int DefaultRetention = 10;
    public const string FilenamePrefix = "collabhost-crash-";
    public const string FilenameExtension = ".log";

    public static string ResolveDirectory(IConfiguration? configuration, string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromConfig = configuration?[ConfigurationDirectoryKey];

        return !string.IsNullOrWhiteSpace(fromConfig)
            ? fromConfig
            : Path.Combine(dataDirectory, DefaultDirectoryName);
    }

    public static int ResolveRetention(IConfiguration? configuration)
    {
        var raw = configuration?[ConfigurationRetentionKey];

        return !string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0
            ? parsed
            : DefaultRetention;
    }

    // Build the file content from the same shape StartupStderr emits, so the on-disk file
    // is recognizable to operators who've seen the stderr block. Adds an exception block
    // when the failure carries one. UTC timestamp at the top so the operator can correlate
    // with system logs (journalctl, Event Viewer, etc.).
    public static string BuildContent
    (
        DateTimeOffset timestampUtc,
        string summary,
        IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<string> recoverySteps,
        int exitCode,
        Exception? exception = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(recoverySteps);

        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        writer.WriteLine("Collabhost crash log");
        writer.WriteLine($"Timestamp (UTC): {timestampUtc.UtcDateTime:O}");
        writer.WriteLine($"Version: {VersionInfo.Current}");
        writer.WriteLine($"Exit code: {exitCode.ToString(CultureInfo.InvariantCulture)}");
        writer.WriteLine();
        writer.WriteLine($"Summary: {summary}");
        writer.WriteLine();

        if (details.Count > 0)
        {
            writer.WriteLine("Details:");

            foreach (var (label, value) in details)
            {
                writer.WriteLine($"  - {label}: {value}");
            }

            writer.WriteLine();
        }

        if (recoverySteps.Count > 0)
        {
            writer.WriteLine("Recovery:");

            for (var index = 0; index < recoverySteps.Count; index++)
            {
                var step = (index + 1).ToString(CultureInfo.InvariantCulture);
                writer.WriteLine($"  {step}. {recoverySteps[index]}");
            }

            writer.WriteLine();
        }

        if (exception is not null)
        {
            writer.WriteLine("Exception:");
            writer.WriteLine(exception.ToString());
            writer.WriteLine();
        }

        return writer.ToString();
    }

    // Try to write a crash file. Best-effort: any IO failure is swallowed (returning null)
    // so a crash-on-write doesn't mask the original failure -- the stderr block is still
    // the operator's primary signal. The directory is created on demand. Filename includes
    // a UTC timestamp; collisions in the same millisecond are theoretically possible but
    // not in practice (single host process, single startup), so no disambiguator suffix.
    public static string? TryWrite
    (
        string directory,
        DateTimeOffset timestampUtc,
        string content,
        int retention = DefaultRetention
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        try
        {
            Directory.CreateDirectory(directory);

            // Filesystem-safe UTC stamp: yyyyMMddTHHmmssZ. Sortable, no colons (Windows-safe).
            var stamp = timestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var fileName = $"{FilenamePrefix}{stamp}{FilenameExtension}";
            var path = Path.Combine(directory, fileName);

            File.WriteAllText(path, content);

            ApplyRetention(directory, retention);

            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed COLLABHOST_LOGS_PATH (invalid path chars). Don't take down the
            // process trying to log a crash.
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    // Keep last N crash logs, prune the rest by descending write time. Best-effort:
    // failures are swallowed since retention is a hygiene concern, not a correctness one.
    public static void ApplyRetention(string directory, int retention)
    {
        if (retention <= 0)
        {
            return;
        }

        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            var pattern = $"{FilenamePrefix}*{FilenameExtension}";
            var files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            if (files.Count <= retention)
            {
                return;
            }

            foreach (var stale in files.Skip(retention))
            {
                try
                {
                    stale.Delete();
                }
                catch (IOException)
                {
                    // Another process may have it open; leave it for the next sweep.
                }
                catch (UnauthorizedAccessException)
                {
                    // Permission denied on this file; skip and continue with the rest.
                }
            }
        }
        catch (IOException)
        {
            // Enumeration failed (transient FS error). Retention is hygiene; skip this sweep.
        }
        catch (UnauthorizedAccessException)
        {
            // Directory not enumerable. Same posture as IOException: skip this sweep.
        }
    }
}
