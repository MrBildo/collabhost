using System.Globalization;

namespace Collabhost.Api.Probes;

// Pulls the binary-at-root attributes the Executable panel renders: name, size,
// candidate count, and the soft-nudge IsManagedDotnet flag. Driven entirely off
// the evidence collector's signal so we have a single source of truth for "this
// looks like .NET" -- card #220 Bill ruling 2.
public static class ExecutableExtractor
{
    public static RawExecutableData? Extract(string artifactDirectory, ArtifactEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var binarySignal = FindBinarySignal(evidence);

        if (binarySignal is null)
        {
            return null;
        }

        var attributes = binarySignal.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var binaryName = attributes.TryGetValue("binaryName", out var nameAttr)
            ? nameAttr
            : binarySignal.Path;

        var candidateCount = attributes.TryGetValue("count", out var countAttr)
            && int.TryParse(countAttr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCount)
                ? parsedCount
                : 1;

        var isManagedDotnet = attributes.TryGetValue("isManagedDotnet", out var managedAttr)
            && string.Equals(managedAttr, "true", StringComparison.Ordinal);

        long sizeBytes = 0;

        try
        {
            var fullPath = Path.Combine(artifactDirectory, binaryName);

            if (File.Exists(fullPath))
            {
                sizeBytes = new FileInfo(fullPath).Length;
            }
        }
        catch (IOException)
        {
            sizeBytes = 0;
        }
        catch (UnauthorizedAccessException)
        {
            sizeBytes = 0;
        }

        return new RawExecutableData(binaryName, sizeBytes, candidateCount, isManagedDotnet);
    }

    private static EvidenceSignal? FindBinarySignal(ArtifactEvidence evidence)
    {
        foreach (var signal in evidence.Signals)
        {
            if (string.Equals(signal.Kind, EvidenceSignalKinds.BinaryAtRoot, StringComparison.Ordinal))
            {
                return signal;
            }
        }

        return null;
    }
}
