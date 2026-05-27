namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> and Dictionary<K,V> are practical for raw data
#pragma warning disable MA0016

// --- .NET Raw Data ---

public record RawDotnetData
(
    RawRuntimeConfig? RuntimeConfig,
    RawDepsJson? DepsJson
);

public record RawRuntimeConfig
(
    string? Tfm,
    List<RawFrameworkReference> Frameworks,
    List<RawFrameworkReference> IncludedFrameworks,
    Dictionary<string, JsonElement> ConfigProperties
);

public record RawFrameworkReference(string Name, string Version);

public record RawDepsJson
(
    string? RuntimeTarget,
    Dictionary<string, RawDepsLibrary> Libraries
);

public record RawDepsLibrary(string Type, string? Version);

// --- Node.js Raw Data ---

public record RawNodeData
(
    RawPackageJson? PackageJson,
    string? DetectedLockfile
);

public record RawPackageJson
(
    string? Name,
    string? Version,
    string? Type,
    string? EngineNode,
    string? PackageManager,
    Dictionary<string, string> Dependencies,
    Dictionary<string, string> DevDependencies
);

// --- TypeScript Raw Data ---

public record RawTypeScriptData
(
    string? Version,
    RawTsConfig? TsConfig
);

public record RawTsConfig
(
    bool? Strict,
    string? Target,
    string? Module
);

// --- Static Site Raw Data ---

public record RawStaticSiteData
(
    bool HasIndexHtml,
    int HtmlFileCount,
    long TotalAssetBytes,
    bool HasNestedAssets
);

// --- Static Site Framework Raw Data (Card #359) ---
//
// Framework / build-tool fingerprint extracted from the built artifact: index.html
// markers, meta-framework output directories (_next, _astro, _app, _nuxt), and
// assets/ filename patterns (content-hashed Vite, runtime~ Webpack). Symmetric
// with the dotnet runtime-config probe -- both read the shipped output, not the
// source repo.
public record RawStaticSiteFrameworkData
(
    string Framework,
    string BuildTool,
    string? MetaFramework,
    string Confidence,
    string EvidenceSource
);

// --- Executable Raw Data ---

public record RawExecutableData
(
    string BinaryName,
    long BinarySizeBytes,
    int CandidateBinaryCount,
    bool IsManagedDotnet
);
