using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.StaticSite;

// Card #377: integration coverage for the fact-gather half of the orphan check
// -- ValidateAsync correctly maps real platform state (resolved capabilities,
// proxy route state, on-disk file) to the three booleans the predicate consumes.
//
// The unsafe triple fires; each legitimate state is silent. Exercised against
// the real AppStore / CapabilityStore / TypeStore / ProxyManager /
// AppDataPathResolver -- the same wiring Program.cs's ApplicationStarted callback
// resolves at boot. State setup mirrors RuntimeConfigFileBootStateTests:
// AppStore.CreateAsync + SaveOverrideAsync, bypassing the REST endpoints so the
// route-state side effects are controlled explicitly per scenario.
[Collection("Api")]
public class RuntimeConfigOverlayOrphanValidateTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly ApiFixture _fixture = fixture;

    private string _artifactDirectory = null!;
    private readonly List<App> _createdApps = [];
    private readonly List<string> _writableDirsToClean = [];

    public ValueTask InitializeAsync()
    {
        _artifactDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-rcf-orphan-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_artifactDirectory);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        var appStore = _fixture.Services.GetRequiredService<AppStore>();

        foreach (var app in _createdApps)
        {
            try
            {
                await appStore.DeleteAppAsync(app.Id, CancellationToken.None);
            }
            catch (Exception)
            {
                // Best-effort cleanup.
            }
        }

        foreach (var dir in _writableDirsToClean)
        {
            TryDeleteDirectory(dir);
        }

        TryDeleteDirectory(_artifactDirectory);
    }

    [Fact]
    public async Task ValidateAsync_RouteActive_ValuesRegistered_FileAbsent_ReportsOrphan()
    {
        var (app, expectedFilePath) = await SeedStaticSiteAsync(values: "https://api.example.com", writeFile: false);

        // Route is active by default: no DisableRoute call, no StoppedByOperator.
        var outcome = await ValidateAsync();

        outcome.Status.ShouldBe(RuntimeConfigOverlayOrphanStatus.OrphansFound);
        outcome.Orphans.ShouldContain(o => o.Slug == app.Slug);

        // Lock the path-drift property: the check's ExpectedFilePath must match the
        // writable-dir target the writer resolves (RuntimeConfigFileWriter.ResolveTargetPath).
        // The two computations are independent; this assertion fails loudly if one drifts
        // from the other, so the operator-facing warning never names a wrong remedy path.
        outcome.Orphans.Single(o => o.Slug == app.Slug).ExpectedFilePath.ShouldBe(expectedFilePath);
    }

    [Fact]
    public async Task ValidateAsync_FilePresent_IsSilent()
    {
        var (app, _) = await SeedStaticSiteAsync(values: "https://api.example.com", writeFile: true);

        var outcome = await ValidateAsync();

        outcome.Orphans.ShouldNotContain(o => o.Slug == app.Slug);
    }

    [Fact]
    public async Task ValidateAsync_NoValuesRegistered_IsSilent()
    {
        // Empty values -> writer no-ops by design; no file is expected on disk.
        var (app, _) = await SeedStaticSiteAsync(values: null, writeFile: false);

        var outcome = await ValidateAsync();

        outcome.Orphans.ShouldNotContain(o => o.Slug == app.Slug);
    }

    [Fact]
    public async Task ValidateAsync_RouteInactive_IsSilent()
    {
        var (app, _) = await SeedStaticSiteAsync(values: "https://api.example.com", writeFile: false);

        // Operator-stopped route: nothing is being served to 404.
        var proxyManager = _fixture.Services.GetRequiredService<ProxyManager>();
        proxyManager.DisableRoute(app.Slug);

        try
        {
            var outcome = await ValidateAsync();

            outcome.Orphans.ShouldNotContain(o => o.Slug == app.Slug);
        }
        finally
        {
            // Restore default-true so the shared ProxyManager state does not leak.
            proxyManager.EnableRoute(app.Slug);
        }
    }

    [Fact]
    public async Task ValidateAsync_NoArtifactDirectory_IsSilent()
    {
        // No artifact location -> LoadRoutableAppsAsync skips the route, so no
        // overlay is emitted; the orphan condition cannot apply.
        var (app, _) = await SeedStaticSiteAsync
        (
            values: "https://api.example.com",
            writeFile: false,
            seedArtifact: false
        );

        var outcome = await ValidateAsync();

        outcome.Orphans.ShouldNotContain(o => o.Slug == app.Slug);
    }

    private async Task<RuntimeConfigOverlayOrphanOutcome> ValidateAsync() =>
        await RuntimeConfigOverlayOrphanCheck.ValidateAsync
        (
            _fixture.Services.GetRequiredService<AppStore>(),
            _fixture.Services.GetRequiredService<CapabilityStore>(),
            _fixture.Services.GetRequiredService<TypeStore>(),
            _fixture.Services.GetRequiredService<ProxyManager>(),
            _fixture.Services.GetRequiredService<AppDataPathResolver>(),
            CancellationToken.None
        );

    // Creates a static-site app directly via AppStore (no REST side effects on
    // _routeStates), optionally seeds the artifact location and a runtime-config
    // values entry, and optionally writes the overlay file to the writable dir.
    private async Task<(App App, string ExpectedFilePath)> SeedStaticSiteAsync
    (
        string? values,
        bool writeFile,
        bool seedArtifact = true
    )
    {
        var appStore = _fixture.Services.GetRequiredService<AppStore>();
        var dataPathResolver = _fixture.Services.GetRequiredService<AppDataPathResolver>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"rcf-orphan-{suffix}";

        var app = new App
        {
            Slug = slug,
            DisplayName = "RCF Orphan Test",
            AppTypeSlug = "static-site",
            StoppedByOperator = false
        };

        await appStore.CreateAsync(app, CancellationToken.None);
        _createdApps.Add(app);

        if (seedArtifact)
        {
            var artifactOverride = new JsonObject
            {
                ["location"] = _artifactDirectory
            };

            await appStore.SaveOverrideAsync
            (
                app.Id,
                "artifact",
                artifactOverride.ToJsonString(),
                CancellationToken.None
            );
        }

        if (values is not null)
        {
            var valuesObject = new JsonObject
            {
                ["apiBaseUrl"] = values
            };
            var rcfOverride = new JsonObject
            {
                ["values"] = valuesObject
            };

            await appStore.SaveOverrideAsync
            (
                app.Id,
                "runtime-config-file",
                rcfOverride.ToJsonString(),
                CancellationToken.None
            );
        }

        appStore.Invalidate(slug);
        appStore.InvalidateOverrides(app.Id);

        // Default capability path is "/config.json"; the writable target strips
        // the leading slash and combines under the writable root.
        var writableRoot = dataPathResolver.ResolveFor(slug);
        var expectedFilePath = Path.Combine(writableRoot, "config.json");
        _writableDirsToClean.Add(writableRoot);

        if (writeFile)
        {
            Directory.CreateDirectory(writableRoot);
            await File.WriteAllTextAsync(expectedFilePath, """{"apiBaseUrl":"https://api.example.com"}""");
        }

        return (app, expectedFilePath);
    }

    private static void TryDeleteDirectory(string dir)
    {
        if (Directory.Exists(dir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
