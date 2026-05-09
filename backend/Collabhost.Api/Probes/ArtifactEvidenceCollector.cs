using System.Globalization;

namespace Collabhost.Api.Probes;

// Single source of truth for "what's in this directory and what does it look like
// for this AppType?" Both the detect-strategy endpoint and the probe pipeline ask
// this question; the collector gives them one answer. Per-AppType rule order is
// load-bearing -- first match wins. Card #220 (Marcus's design doc).
//
// Pure static -- no I/O abstractions, no DI. The Filesystem endpoint and Probe
// pipeline both call into this directly.
public static class ArtifactEvidenceCollector
{
    public static ArtifactEvidence Collect(string directory, string appTypeSlug) =>
        !Directory.Exists(directory)
            ? Empty(SuggestedStrategies.Manual)
            : appTypeSlug switch
            {
                "dotnet-app" => CollectDotnet(directory),
                "nodejs-app" => CollectNodeJs(directory),
                "static-site" => CollectStaticSite(directory),
                "executable" => CollectExecutable(directory),
                _ => Empty(SuggestedStrategies.Manual)
            };

    // --- dotnet-app -----------------------------------------------------------

    private static ArtifactEvidence CollectDotnet(string directory)
    {
        var runtimeConfigs = SafeGetFiles(directory, "*.runtimeconfig.json");

        if (runtimeConfigs.Length > 0)
        {
            return new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.DotNetRuntimeConfiguration,
                [.. runtimeConfigs.Select(f => Signal(EvidenceSignalKinds.RuntimeConfig, Path.GetFileName(f)))],
                RuntimeFamilies.Dotnet
            );
        }

        var projects = SafeGetFiles(directory, "*.csproj");

        if (projects.Length > 0)
        {
            return new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.DotNetProject,
                [.. projects.Select(f => Signal(EvidenceSignalKinds.ProjectFile, Path.GetFileName(f)))],
                RuntimeFamilies.Dotnet
            );
        }

        // Self-contained single-file publish detection. Three signals, ordered by
        // cost. Stop at first decisive match.
        var singleFileSignals = DetectSingleFileBinarySignals(directory);

        if (singleFileSignals.Count > 0)
        {
            return new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.Manual,
                singleFileSignals,
                RuntimeFamilies.Dotnet
            );
        }

        // Partial-publish hint: wwwroot/ or staticwebassets manifest at root with
        // no binary detected. The operator pointed at a directory that LOOKS .NET
        // but doesn't have a launch entry. LikelyMatch with manual strategy.
        var partialSignals = DetectStaticAssetSignals(directory);

        return partialSignals.Count > 0
            ? new ArtifactEvidence
            (
                AppTypeFitness.LikelyMatch,
                SuggestedStrategies.Manual,
                partialSignals,
                RuntimeFamilies.Dotnet
            )
            : Empty(SuggestedStrategies.Manual);
    }

    // --- nodejs-app -----------------------------------------------------------

    private static ArtifactEvidence CollectNodeJs(string directory)
    {
        var packageJsonPath = Path.Combine(directory, "package.json");

        if (!File.Exists(packageJsonPath))
        {
            return Empty(SuggestedStrategies.Manual);
        }

        var hasStart = TryReadHasStartScript(packageJsonPath);

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hasStart"] = hasStart ? "true" : "false"
        };

        IReadOnlyList<EvidenceSignal> signals =
        [
            new EvidenceSignal(EvidenceSignalKinds.PackageJson, "package.json", attributes)
        ];

        return hasStart
            ? new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.PackageJson,
                signals,
                RuntimeFamilies.Node
            )
            : new ArtifactEvidence
            (
                AppTypeFitness.LikelyMatch,
                SuggestedStrategies.Manual,
                signals,
                RuntimeFamilies.Node
            );
    }

    // --- static-site ----------------------------------------------------------

    private static ArtifactEvidence CollectStaticSite(string directory)
    {
        var indexHtml = Path.Combine(directory, "index.html");

        if (File.Exists(indexHtml))
        {
            return new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.NotApplicable,
                [Signal(EvidenceSignalKinds.IndexHtml, "index.html")],
                RuntimeFamilies.Static
            );
        }

        var alternateEntries = _alternateStaticEntries
            .Where(name => File.Exists(Path.Combine(directory, name)))
                .ToArray();

        if (alternateEntries.Length > 0)
        {
            return new ArtifactEvidence
            (
                AppTypeFitness.LikelyMatch,
                SuggestedStrategies.NotApplicable,
                [.. alternateEntries.Select(n => Signal(EvidenceSignalKinds.IndexHtml, n))],
                RuntimeFamilies.Static
            );
        }

        var htmlFiles = SafeGetFiles(directory, "*.html");

        if (htmlFiles.Length > 0)
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["count"] = htmlFiles.Length.ToString(CultureInfo.InvariantCulture)
            };

            return new ArtifactEvidence
            (
                AppTypeFitness.LikelyMatch,
                SuggestedStrategies.NotApplicable,
                [new EvidenceSignal(EvidenceSignalKinds.HtmlFiles, Path.GetFileName(htmlFiles[0]), attributes)],
                RuntimeFamilies.Static
            );
        }

        return Empty(SuggestedStrategies.NotApplicable, RuntimeFamilies.Static);
    }

    // --- executable -----------------------------------------------------------

    private static ArtifactEvidence CollectExecutable(string directory)
    {
        var executables = ListExecutablesAtRoot(directory);

        if (executables.Count == 0)
        {
            return Empty(SuggestedStrategies.NotApplicable, RuntimeFamilies.Executable);
        }

        // Bridge to .NET single-file detection: if the lone (or first) executable
        // is a self-contained .NET single-file publish, surface that as an
        // attribute on the binary-at-root signal so the curator can render the
        // soft-nudge banner. Bill ruling 2: single panel + nudge, NOT side-by-side.
        var primaryBinary = executables[0];
        var isManagedDotnet = LooksLikeDotnetBinary(directory, primaryBinary);

        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["count"] = executables.Count.ToString(CultureInfo.InvariantCulture),
            ["binaryName"] = primaryBinary,
            ["isManagedDotnet"] = isManagedDotnet ? "true" : "false"
        };

        var signal = new EvidenceSignal
        (
            EvidenceSignalKinds.BinaryAtRoot,
            primaryBinary,
            attributes
        );

        return executables.Count == 1
            ? new ArtifactEvidence
            (
                AppTypeFitness.FullMatch,
                SuggestedStrategies.Manual,
                [signal],
                RuntimeFamilies.Executable
            )
            : new ArtifactEvidence
            (
                AppTypeFitness.LikelyMatch,
                SuggestedStrategies.Manual,
                [signal],
                RuntimeFamilies.Executable
            );
    }

    // --- helpers --------------------------------------------------------------

    internal static IReadOnlyList<EvidenceSignal> DetectSingleFileBinarySignals(string directory)
    {
        var staticAssetsManifest = SafeGetFiles(directory, "*.staticwebassets.endpoints.json");

        // Try cheap signals first: pdb-pair (cheapest), then static-asset manifest.
        var pdbPair = TryDetectPdbPair(directory);

        if (pdbPair is not null)
        {
            var signals = new List<EvidenceSignal>
            {
                new(EvidenceSignalKinds.SingleFileBinary, pdbPair.Value.BinaryName, null),
                new(EvidenceSignalKinds.PdbPair, pdbPair.Value.PdbName, null)
            };

            if (staticAssetsManifest.Length > 0)
            {
                signals.Add
                (
                    Signal(EvidenceSignalKinds.StaticAssetManifest, Path.GetFileName(staticAssetsManifest[0]))
                );
            }

            return signals;
        }

        if (staticAssetsManifest.Length > 0)
        {
            // Static asset manifest at root + a binary -- ASP.NET Core publish.
            var binary = TryFindCandidateBinary(directory);

            if (binary is not null)
            {
                return
                [
                    new(EvidenceSignalKinds.SingleFileBinary, binary, null),
                    Signal(EvidenceSignalKinds.StaticAssetManifest, Path.GetFileName(staticAssetsManifest[0]))
                ];
            }
        }

        // Fall back to magic-bytes scan -- expensive (file I/O) so last.
        var bundleBinary = TryDetectBundleSignature(directory);

        return bundleBinary is not null
            ? [new EvidenceSignal(EvidenceSignalKinds.SingleFileBinary, bundleBinary, null)]
            : [];
    }

    private static List<EvidenceSignal> DetectStaticAssetSignals(string directory)
    {
        var signals = new List<EvidenceSignal>();

        var manifests = SafeGetFiles(directory, "*.staticwebassets.endpoints.json");

        foreach (var manifest in manifests)
        {
            signals.Add(Signal(EvidenceSignalKinds.StaticAssetManifest, Path.GetFileName(manifest)));
        }

        if (Directory.Exists(Path.Combine(directory, "wwwroot")))
        {
            signals.Add(Signal(EvidenceSignalKinds.Wwwroot, "wwwroot"));
        }

        return signals;
    }

    private static (string BinaryName, string PdbName)? TryDetectPdbPair(string directory)
    {
        var pdbs = SafeGetFiles(directory, "*.pdb");

        foreach (var pdbPath in pdbs)
        {
            var stem = Path.GetFileNameWithoutExtension(pdbPath);

            // Look for a managed binary next to the pdb. Windows uses name.exe,
            // Linux uses name with the executable bit. We don't enforce the exec
            // bit here because the host filesystem may not preserve Linux
            // permissions (a Linux publish placed on a Windows host for
            // inspection still ships pdbs). The pdb-next-to-binary pair is the
            // durable .NET signal regardless of platform.
            var exePath = Path.Combine(directory, stem + ".exe");

            if (File.Exists(exePath))
            {
                return (stem + ".exe", Path.GetFileName(pdbPath));
            }

            var bareBinary = Path.Combine(directory, stem);

            if (File.Exists(bareBinary))
            {
                return (stem, Path.GetFileName(pdbPath));
            }
        }

        return null;
    }

    private static string? TryFindCandidateBinary(string directory)
    {
        // Prefer .exe on any platform (publish output for win-* RIDs ships .exe).
        var exes = SafeGetFiles(directory, "*.exe");

        if (exes.Length > 0)
        {
            return Path.GetFileName(exes[0]);
        }

        // Otherwise look for any non-extension executable bit on Linux.
        return TryFindExtensionlessBinary(directory);
    }

    private static string? TryFindExtensionlessBinary(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (Path.HasExtension(file))
                {
                    continue;
                }

                if (OperatingSystem.IsWindows() || HasExecutableBit(file))
                {
                    return Path.GetFileName(file);
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? TryDetectBundleSignature(string directory)
    {
        // Scan candidate binaries for the bundle signature. Bound to first
        // match. We don't need to scan every file in the directory.
        var candidates = new List<string>();

        candidates.AddRange(SafeGetFiles(directory, "*.exe"));

        var extensionless = TryFindExtensionlessBinary(directory);

        if (extensionless is not null)
        {
            candidates.Add(Path.Combine(directory, extensionless));
        }

        foreach (var candidate in candidates)
        {
            if (SingleFileBundleReader.TryRead(candidate) is not null)
            {
                return Path.GetFileName(candidate);
            }
        }

        return null;
    }

    private static List<string> ListExecutablesAtRoot(string directory)
    {
        var results = new List<string>();

        try
        {
            if (OperatingSystem.IsWindows())
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.exe"))
                {
                    results.Add(Path.GetFileName(path));
                }
            }
            else
            {
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    var fileName = Path.GetFileName(path);
                    var extension = Path.GetExtension(fileName);

                    // Exclude obvious script extensions -- Manual strategy with an
                    // explicit interpreter is the right shape for those.
                    if (_excludedExtensions.Contains(extension))
                    {
                        continue;
                    }

                    if (HasExecutableBit(path))
                    {
                        results.Add(fileName);
                    }
                }
            }
        }
        catch (IOException)
        {
            return results;
        }
        catch (UnauthorizedAccessException)
        {
            return results;
        }

        results.Sort(StringComparer.Ordinal);

        return results;
    }

    private static readonly FrozenSet<string> _excludedExtensions = FrozenSet.ToFrozenSet
    (
        [".sh", ".bash", ".py", ".rb", ".pl", ".js", ".mjs", ".cjs", ".ts", ".lua"],
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly string[] _alternateStaticEntries = ["index.htm", "default.html"];

    // Returns true only when the path is a regular file with the user-execute
    // bit set. The Length filter excludes Linux FIFOs, sockets, and device
    // files -- they stat as zero length. Without that filter, the /tmp
    // directory on a Linux CI runner contains clr-debug-pipe FIFOs from the
    // test host (extensionless, executable bits set); a downstream File.OpenRead
    // on a FIFO blocks waiting for a writer, which is what hung the Linux leg
    // of card 220. A real single-file bundle is megabytes, so the Length floor
    // is safe for binary detection. Test fixtures writing tiny .exe files are
    // routed through the Windows .exe-only branch and do not pass through here.
    private static bool HasExecutableBit(string path)
    {
        try
        {
            var info = new FileInfo(path);

            if ((info.UnixFileMode & UnixFileMode.UserExecute) != UnixFileMode.UserExecute)
            {
                return false;
            }

            // Pipes, sockets, devices, and zero-length files cannot be a
            // single-file bundle -- gate them out before any caller calls
            // File.OpenRead on the path.
            return info.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool LooksLikeDotnetBinary(string directory, string binaryName)
    {
        var pdbCandidate = Path.Combine
        (
            directory,
            Path.GetFileNameWithoutExtension(binaryName) + ".pdb"
        );

        if (File.Exists(pdbCandidate))
        {
            return true;
        }

        var staticAssets = SafeGetFiles(directory, "*.staticwebassets.endpoints.json");

        if (staticAssets.Length > 0)
        {
            return true;
        }

        var binaryPath = Path.Combine(directory, binaryName);

        return File.Exists(binaryPath) && SingleFileBundleReader.TryRead(binaryPath) is not null;
    }

    private static bool TryReadHasStartScript(string packageJsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJsonPath));

            return document.RootElement.TryGetProperty("scripts", out var scripts)
                && scripts.TryGetProperty("start", out _);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string[] SafeGetFiles(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static EvidenceSignal Signal(string kind, string path) =>
        new(kind, path, null);

    private static ArtifactEvidence Empty(string suggestedStrategy, string? runtimeFamily = null) =>
        new(AppTypeFitness.NotApplicable, suggestedStrategy, [], runtimeFamily);
}
