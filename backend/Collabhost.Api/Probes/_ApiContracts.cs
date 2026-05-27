namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for response types
#pragma warning disable MA0016

public record ProbeEntry(string Type, string Label, object Data);

// Cache lifecycle state surfaced to the API boundary (Card #337). Distinguishes
// the three observationally-indistinguishable empty states that `Entries=[]`
// alone could not separate:
//   - NotApplicable : the app's AppType is not in the probe-applicable set
//     (today: `system-service`). Entries will always be empty by curation
//     policy; the frontend should render this differently from "we haven't
//     looked yet."
//   - NeverProbed   : no cache entry exists for this app id. Either the app
//     is brand-new (registered after the last periodic tick) or the periodic
//     service has not yet completed its first sweep for this app.
//   - Fresh         : the cache holds an entry written within the freshness
//     window. Entries may still be empty (legitimately empty extraction --
//     e.g. dotnet-app with an artifact directory containing nothing the
//     extractors recognize); the status distinguishes this from NeverProbed.
//   - Stale         : the cache holds an entry written outside the freshness
//     window. Under normal Option-B operation the periodic refresher runs at
//     half the freshness window, so Stale should not appear -- when it does,
//     the periodic loop has stopped advancing and the operator should see an
//     explicit signal rather than blank panels.
public enum ProbeCacheStatus
{
    Fresh,
    Stale,
    NeverProbed,
    NotApplicable
}

public record ProbeCacheResult(ProbeCacheStatus Status, List<ProbeEntry> Entries);

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

// --- Static Site Framework (Card #359) ---
//
// Surfaced as the `static-site-framework` probe entry, distinct from the generic
// `static-site` shape probe. Lights up when the curator can identify the
// framework or build tool from the shipped artifact -- nodejs-app's React panel
// stays package.json-keyed and unchanged. The fields are intentionally surface
// in plain English so the operator can read them in the Technology pane without
// a dictionary:
//   Framework        -- react / vue / svelte / astro / static-only / unknown
//   BuildTool        -- vite / webpack / parcel / esbuild / unbundled / unknown
//   MetaFramework    -- next / nuxt / astro / sveltekit / remix (or null)
//   Confidence       -- high / medium / low
//   EvidenceSource   -- which fingerprint matched, surfaced verbatim
public record StaticSiteFrameworkData
(
    string Framework,
    string BuildTool,
    string? MetaFramework,
    string Confidence,
    string EvidenceSource
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
