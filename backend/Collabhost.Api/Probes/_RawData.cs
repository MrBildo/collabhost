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
