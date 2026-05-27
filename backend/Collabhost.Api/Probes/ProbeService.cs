using System.Collections.Concurrent;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for probe results
#pragma warning disable MA0016

public class ProbeService
(
    AppStore appStore,
    CapabilityStore capabilityStore,
    TimeProvider timeProvider,
    ILogger<ProbeService> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly TimeProvider _timeProvider = timeProvider
        ?? throw new ArgumentNullException(nameof(timeProvider));

    private readonly ILogger<ProbeService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<Ulid, ProbeCacheEntry> _cache = new();

    // Freshness window. An entry written more than this ago is reported as Stale.
    // The periodic refresher (ProbePeriodicService) runs at half this cadence so
    // under normal operation entries cycle from Fresh -> Fresh, never Stale.
    // Stale is the operator-facing signal that the periodic loop has stopped
    // advancing. Internal-with-init so tests can override.
    internal TimeSpan FreshnessWindow { get; init; } = TimeSpan.FromMinutes(30);

    public ProbeCacheResult GetCachedProbes(Ulid appId, string appTypeSlug)
    {
        if (!IsProbeApplicable(appTypeSlug))
        {
            return new ProbeCacheResult(ProbeCacheStatus.NotApplicable, []);
        }

        if (!_cache.TryGetValue(appId, out var entry))
        {
            return new ProbeCacheResult(ProbeCacheStatus.NeverProbed, []);
        }

        var age = _timeProvider.GetUtcNow() - entry.SetAt;
        var status = ClassifyAge(age, FreshnessWindow);

        return new ProbeCacheResult(status, entry.Entries);
    }

    // Pure state-classifier extracted from GetCachedProbes so the cache-lifecycle
    // states can be exercised without standing up the full DI graph. The caller
    // is responsible for the NotApplicable / NeverProbed branches; this maps a
    // cached entry's age into Fresh-vs-Stale.
    internal static ProbeCacheStatus ClassifyAge(TimeSpan age, TimeSpan freshnessWindow) =>
        age <= freshnessWindow
            ? ProbeCacheStatus.Fresh
            : ProbeCacheStatus.Stale;

    // The set of AppType slugs whose curation policy can ever produce probe
    // entries. Mirrors the allow* flags in ProbeCurator.Curate. Currently
    // excludes `system-service` (no probe panels apply). Unknown/user-defined
    // AppType slugs default to applicable -- the curator (not this filter) is
    // the authoritative gate on whether any panel comes out.
    internal static bool IsProbeApplicable(string appTypeSlug) =>
        appTypeSlug switch
        {
            "system-service" => false,
            _ => true
        };

    public async Task RunProbesAsync(Ulid appId, CancellationToken ct)
    {
        var app = await _appStore.GetByIdAsync(appId, ct);

        if (app is null)
        {
            return;
        }

        var artifactConfiguration = await _capabilityStore.ResolveAsync<ArtifactConfiguration>
        (
            "artifact", app, ct
        );

        if (artifactConfiguration is null || string.IsNullOrWhiteSpace(artifactConfiguration.Location))
        {
            return;
        }

        var probes = RunProbesForDirectory
        (
            artifactConfiguration.Location,
            artifactConfiguration.ProjectRoot,
            app.AppTypeSlug
        );

        _cache[appId] = new ProbeCacheEntry(_timeProvider.GetUtcNow(), probes);

        _logger.LogDebug
        (
            "Probed app '{Slug}' -- {Count} results",
            app.Slug,
            probes.Count
        );
    }

    public async Task RunProbesForAllAppsAsync(CancellationToken ct)
    {
        var apps = await _appStore.ListAsync(ct);

        // Prune cache entries for apps that no longer exist. Mirrors the pattern
        // ProcessResourceSamplerService uses for stopped processes -- the periodic
        // service is the natural owner of cleanup since it walks the live app set.
        var liveAppIds = apps.Select(a => a.Id).ToHashSet();

        foreach (var staleAppId in _cache.Keys.Where(id => !liveAppIds.Contains(id)).ToList())
        {
            _cache.TryRemove(staleAppId, out _);
        }

        foreach (var app in apps)
        {
            try
            {
                await RunProbesAsync(app.Id, ct);
            }
            catch (Exception exception)
            {
                _logger.LogWarning
                (
                    exception,
                    "Failed to probe app '{Slug}'",
                    app.Slug
                );
            }
        }
    }

    public void InvalidateProbeCache(Ulid appId) =>
        _cache.TryRemove(appId, out _);

    // Pre-card-#220 entry point retained for tests that don't supply an AppType.
    // The new entry point routes through the evidence collector so detection and
    // curation share the same view of the directory.
    internal static List<ProbeEntry> RunProbesForDirectory
    (
        string artifactDirectory,
        string? projectRoot
    ) => RunProbesForDirectory(artifactDirectory, projectRoot, appTypeSlug: null);

    internal static List<ProbeEntry> RunProbesForDirectory
    (
        string artifactDirectory,
        string? projectRoot,
        string? appTypeSlug
    )
    {
        var evidence = appTypeSlug is null
            ? null
            : ArtifactEvidenceCollector.Collect(artifactDirectory, appTypeSlug);

        var dotnet = DotnetExtractor.Extract(artifactDirectory, evidence);
        var node = NodeExtractor.Extract(projectRoot, artifactDirectory);

        var typeScript = node?.PackageJson is not null
            ? TypeScriptExtractor.Extract(node.PackageJson, projectRoot, artifactDirectory)
            : TypeScriptExtractor.Extract(null, projectRoot, artifactDirectory);

        var staticSite = StaticSiteExtractor.Extract(artifactDirectory);

        // Card #359 -- built-output framework fingerprint. Always run the
        // extractor; the curator's allowStatic gate is the policy decision
        // about whether to surface the result. Running unconditionally keeps
        // the extractor-vs-curator split clean (extractors are policy-free).
        var staticSiteFramework = StaticSiteFrameworkExtractor.Extract(artifactDirectory);

        var executable = evidence is not null
            ? ExecutableExtractor.Extract(artifactDirectory, evidence)
            : null;

        return ProbeCurator.Curate
        (
            appTypeSlug,
            evidence,
            dotnet,
            node,
            typeScript,
            staticSite,
            staticSiteFramework,
            executable,
            projectRoot,
            artifactDirectory
        );
    }

    // Sealed: private nested cache record, not part of the public surface,
    // not designed for extension.
    private sealed record ProbeCacheEntry(DateTimeOffset SetAt, List<ProbeEntry> Entries);
}
