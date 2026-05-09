namespace Collabhost.Api.Probes;

// JSON-serializable -- IReadOnlyList/Dictionary surfaces are practical for projection
#pragma warning disable MA0016

// ArtifactEvidence is the single source of truth shared by the detect-strategy endpoint
// and the probe pipeline. The collector emits one of these per (directory, appTypeSlug)
// pair. The detect-strategy endpoint projects to its existing wire shape; the probe
// pipeline feeds the signals into AppType-aware extractors and curators.
//
// Card #220.

public record ArtifactEvidence
(
    AppTypeFitness Fitness,
    string SuggestedStrategy,
    IReadOnlyList<EvidenceSignal> Signals,
    string? RuntimeFamily
);

public record EvidenceSignal
(
    string Kind,
    string Path,
    IReadOnlyDictionary<string, string>? Attributes
);

public enum AppTypeFitness
{
    FullMatch,
    LikelyMatch,
    NotApplicable
}

// Wire-side string literals for SuggestedStrategy. The DiscoveryStrategy enum is
// process-discovery vocabulary -- "notApplicable" is intentionally NOT a member of
// it; the collector emits it as a free string at the API boundary for AppTypes that
// don't go through DiscoveryStrategyExecutor (static-site, executable-with-no-binary).
// Card #220 Bill ruling 1.
public static class SuggestedStrategies
{
    public const string DotNetRuntimeConfiguration = "dotNetRuntimeConfiguration";
    public const string DotNetProject = "dotNetProject";
    public const string PackageJson = "packageJson";
    public const string Manual = "manual";
    public const string NotApplicable = "notApplicable";
}

// Signal kind constants. Stable, documented values consumers can grep.
public static class EvidenceSignalKinds
{
    public const string RuntimeConfig = "runtime-config";
    public const string ProjectFile = "project-file";
    public const string SingleFileBinary = "single-file-binary";
    public const string StaticAssetManifest = "static-asset-manifest";
    public const string Wwwroot = "wwwroot";
    public const string PackageJson = "package-json";
    public const string IndexHtml = "index-html";
    public const string HtmlFiles = "html-files";
    public const string BinaryAtRoot = "binary-at-root";
    public const string PdbPair = "pdb-pair";
}

public static class RuntimeFamilies
{
    public const string Dotnet = "dotnet";
    public const string Node = "node";
    public const string Static = "static";
    public const string Executable = "executable";
}
