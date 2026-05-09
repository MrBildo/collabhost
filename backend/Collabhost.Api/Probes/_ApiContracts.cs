namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for response types
#pragma warning disable MA0016

public record ProbeEntry(string Type, string Label, object Data);

// --- .NET Runtime ---

public record DotnetRuntimeData
(
    string Tfm,
    string RuntimeVersion,
    bool IsAspNetCore,
    bool IsSelfContained,
    bool ServerGc
);

// --- .NET Dependencies ---

public record DotnetDependenciesData
(
    int PackageCount,
    int ProjectReferenceCount,
    List<NotableDependency> Notable
);

public record NotableDependency(string Name, string? Version);

// --- Node.js ---

public record NodeData
(
    string? EngineVersion,
    string? PackageManager,
    string? PackageManagerVersion,
    string? ModuleSystem,
    int DependencyCount,
    int DevDependencyCount
);

// --- React ---

public record ReactData
(
    string Version,
    string? Bundler,
    string? BundlerVersion,
    string? Router,
    string? StateManagement,
    string? CssStrategy
);

// --- TypeScript ---

public record TypeScriptData
(
    string? Version,
    bool Strict,
    string? Target,
    string? Module
);

// --- Static Site ---

public record StaticSiteData
(
    bool HasIndexHtml,
    int HtmlFileCount,
    long TotalAssetBytes,
    bool HasNestedAssets
);

// --- Executable ---
//
// IsManagedDotnet is the soft-nudge channel for the executable-detected-as-.NET
// case (Bill ruling 2 on card #220). When true, the operator registered the app
// as `executable` but the directory contents look like a self-contained .NET
// publish -- the panel surfaces a "consider re-registering as dotnet-app"
// hint. Single panel only -- NOT side-by-side with dotnet-runtime.
public record ExecutableData
(
    string BinaryName,
    long BinarySizeBytes,
    int CandidateBinaryCount,
    bool IsManagedDotnet
);
