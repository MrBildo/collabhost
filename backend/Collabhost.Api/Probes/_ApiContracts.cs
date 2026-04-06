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
