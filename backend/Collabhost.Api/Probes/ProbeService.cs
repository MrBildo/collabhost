using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for probe results
#pragma warning disable MA0016
#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive -- cache key interpolation is safe

public class ProbeService
(
    AppStore appStore,
    CapabilityStore capabilityStore,
    IMemoryCache cache,
    ILogger<ProbeService> logger
)
{
    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly IMemoryCache _cache = cache
        ?? throw new ArgumentNullException(nameof(cache));

    private readonly ILogger<ProbeService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly TimeSpan _probeCacheDuration = TimeSpan.FromMinutes(30);

    public List<ProbeEntry> GetCachedProbes(Ulid appId) =>
        _cache.TryGetValue<List<ProbeEntry>>($"probe:{appId}", out var cached) && cached is not null
            ? cached
            : [];

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

        _cache.Set($"probe:{appId}", probes, _probeCacheDuration);

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
        _cache.Remove($"probe:{appId}");

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
            executable,
            projectRoot,
            artifactDirectory
        );
    }
}
