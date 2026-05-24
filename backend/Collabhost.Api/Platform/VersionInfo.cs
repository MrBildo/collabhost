using System.Reflection;
using System.Runtime.InteropServices;

namespace Collabhost.Api.Platform;

public static class VersionInfo
{
    // Key used by AssemblyMetadataAttribute to embed the wwwroot content hash at
    // archive-build time. Read via reflection by _wwwrootHash below. See
    // PortalIntegrityCheck for the hash algorithm and .github/workflows/publish.yml
    // for the build-side computation. Card #342.
    public const string WwwrootHashMetadataKey = "WwwrootHash";

    private static readonly Lazy<string> _version = new(Compute);

    private static readonly Lazy<string> _commit = new(ComputeCommit);

    private static readonly Lazy<string> _wwwrootHash = new(ComputeWwwrootHash);

    public static string Current => _version.Value;

    public static string Commit => _commit.Value;

    public static string Platform => RuntimeInformation.RuntimeIdentifier;

    // Empty string when no AssemblyMetadataAttribute carrying WwwrootHashMetadataKey is
    // present (dev builds, pre-#342 archives). PortalIntegrityCheck treats empty as
    // Unknown -- silent, no warning.
    public static string WwwrootHash => _wwwrootHash.Value;

    public static string StripCommitHash(string raw)
    {
        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? raw[..plusIndex] : raw;
    }

    private static string Compute()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        return StripCommitHash(raw);
    }

    private static string ComputeCommit()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "";

        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? raw[(plusIndex + 1)..] : "";
    }

    private static string ComputeWwwrootHash()
    {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attr => string.Equals(attr.Key, WwwrootHashMetadataKey, StringComparison.Ordinal));

        return metadata?.Value ?? "";
    }
}
