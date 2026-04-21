namespace Collabhost.Api.Platform;

public static partial class BootVersionTracker
{
    public const string SentinelFileName = ".last-boot-version";
    public const string UnknownVersion = "unknown";

    // Permissive semver-ish pattern. We accept an optional leading 'v' plus dotted numerics
    // with optional pre-release / build tags. Malformed contents fall back to "unknown" per §6.2.1.
    // Length-bounded to protect against pathological inputs.
    [GeneratedRegex(@"^v?\d{1,4}\.\d{1,4}\.\d{1,4}(?:[-+][0-9A-Za-z.\-]{1,40})?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemverPattern { get; }

    public static string Read(string dataDirectory, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        var path = Path.Combine(dataDirectory, SentinelFileName);

        if (!File.Exists(path))
        {
            return UnknownVersion;
        }

        try
        {
            var raw = File.ReadAllText(path).Trim();

            if (string.IsNullOrEmpty(raw) || !SemverPattern.IsMatch(raw))
            {
                logger?.LogWarning
                (
                    "Last-boot-version sentinel at {Path} has malformed contents; treating as unknown",
                    path
                );

                return UnknownVersion;
            }

            return raw;
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Failed to read last-boot-version sentinel at {Path}; treating as unknown", path);
            return UnknownVersion;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, "Failed to read last-boot-version sentinel at {Path}; treating as unknown", path);
            return UnknownVersion;
        }
    }

    public static void Write(string dataDirectory, string version, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var path = Path.Combine(dataDirectory, SentinelFileName);
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, version + Environment.NewLine);

            // Atomic replace on POSIX; overwrite on Windows via File.Move with overwrite.
            File.Move(tempPath, path, overwrite: true);
        }
        catch (IOException ex)
        {
            logger?.LogWarning(ex, "Failed to write last-boot-version sentinel at {Path}", path);
            TryDeleteTemp(tempPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger?.LogWarning(ex, "Failed to write last-boot-version sentinel at {Path}", path);
            TryDeleteTemp(tempPath);
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; ignore.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup; ignore.
        }
    }
}
