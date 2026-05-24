using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Collabhost.Api.Portal;

// Boot-time soft integrity check for the Portal's static-asset bundle. Compares the
// on-disk wwwroot/ content hash against the hash that was embedded into the binary at
// archive-build time (AssemblyMetadataAttribute keyed "WwwrootHash"). Mismatch fires a
// LogWarning -- never halts boot. Same posture as PortalReachabilityCheck: legitimate
// operator customization of wwwroot/ exists, halting on Drift would trade degraded
// mode for fully unreachable. The check is the binary-level signal the UAT runbook
// asserts from outside via wwwroot.sha256 sidecar comparison. Card #342.
//
// Hash shape (must match .github/workflows/publish.yml "Compute wwwroot hash" step):
//   1. Enumerate every file recursively under wwwroot/.
//   2. Relative POSIX paths (forward slashes, no leading slash), ordinal-sorted.
//   3. For each file: hash-update with "<relativePath>\n<sizeBytes>\n<contentSha256Hex>\n".
//   4. Final SHA-256 hex digest is the wwwroot hash.
// File metadata (mtime, owner, mode) is excluded -- tarball extraction sets mtime to
// extract-time on most platforms, which would make a metadata-inclusive hash unstable.
public static class PortalIntegrityCheck
{
    public static PortalIntegrityOutcome Validate
    (
        string baseDirectory,
        string expectedHash,
        ILogger logger
    )
    {
        ArgumentNullException.ThrowIfNull(logger);

        var wwwrootPath = Path.Combine(baseDirectory, "wwwroot");

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            // No build-stamped hash on this binary. Dev builds (dotnet run, aspire start)
            // and any archive published before #342 land here. Silent by design -- a
            // warning would be noise across every non-release build.
            return new PortalIntegrityOutcome
            (
                PortalIntegrityStatus.Unknown,
                wwwrootPath,
                ExpectedHash: "",
                ActualHash: "",
                RecoverySteps: []
            );
        }

        if (!Directory.Exists(wwwrootPath))
        {
            // PortalReachabilityCheck already warned on this state; we do not double-warn.
            // Report Unknown rather than Drift -- there is no actual content to compare.
            return new PortalIntegrityOutcome
            (
                PortalIntegrityStatus.Unknown,
                wwwrootPath,
                ExpectedHash: expectedHash,
                ActualHash: "",
                RecoverySteps: []
            );
        }

        var actualHash = ComputeWwwrootHash(wwwrootPath);

        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase)
            ? new PortalIntegrityOutcome
            (
                PortalIntegrityStatus.Ok,
                wwwrootPath,
                ExpectedHash: expectedHash,
                ActualHash: actualHash,
                RecoverySteps: []
            )
            : new PortalIntegrityOutcome
            (
                PortalIntegrityStatus.Drift,
                wwwrootPath,
                ExpectedHash: expectedHash,
                ActualHash: actualHash,
                RecoverySteps:
                [
                    "Re-extract the release archive to restore the shipped wwwroot/ contents.",
                    "If you customized wwwroot/ intentionally, the drift warning is expected and may be ignored."
                ]
            );
    }

    // Deterministic content hash over the wwwroot/ tree. Public so test code and (if ever
    // needed) operator tooling can compute the same hash the workflow's bash step computes.
    public static string ComputeWwwrootHash(string wwwrootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wwwrootPath);

        var files = Directory.EnumerateFiles(wwwrootPath, "*", SearchOption.AllDirectories)
            .Select(absolute => ToPosixRelative(wwwrootPath, absolute))
            .Order(StringComparer.Ordinal)
            .ToArray();

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var relative in files)
        {
            var absolute = Path.Combine(wwwrootPath, relative.Replace('/', Path.DirectorySeparatorChar));
            var size = new FileInfo(absolute).Length;
            var contentHash = ComputeFileSha256Hex(absolute);

            var line = string.Create
            (
                CultureInfo.InvariantCulture,
                $"{relative}\n{size}\n{contentHash}\n"
            );

            aggregate.AppendData(Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    private static string ToPosixRelative(string root, string absolutePath)
    {
        var relative = Path.GetRelativePath(root, absolutePath);
        return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string ComputeFileSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(stream);
        return Convert.ToHexStringLower(bytes);
    }
}
