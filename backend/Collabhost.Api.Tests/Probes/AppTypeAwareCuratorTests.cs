using System.Text.Json;

using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #220 -- AppType-aware curator filters and the executable-detected-as-.NET
// soft-nudge case.
public class AppTypeAwareCuratorTests
{
    [Fact]
    public void Curate_StaticSiteAppType_SuppressesNodePanelEvenIfPackageJsonPresent()
    {
        // A built React static-site bundle may still have a manifest-shaped
        // package.json artifact. Without AppType-aware filtering the curator
        // would emit a Node panel; with it, only the static-site panel renders.
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                Name: "leftover",
                Version: "1.0.0",
                Type: null,
                EngineNode: null,
                PackageManager: null,
                Dependencies: [],
                DevDependencies: []
            ),
            DetectedLockfile: null
        );

        var staticSite = new RawStaticSiteData
        (
            HasIndexHtml: true,
            HtmlFileCount: 1,
            TotalAssetBytes: 12345,
            HasNestedAssets: true
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "static-site",
            dotnet: null,
            node: node,
            typeScript: null,
            staticSite: staticSite,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.ShouldContain(p => p.Type == "static-site");
        results.ShouldNotContain(p => p.Type == "node");
    }

    [Fact]
    public void Curate_DotnetAppType_DoesNotEmitExecutablePanel()
    {
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig("net10.0", [], [], new Dictionary<string, JsonElement>(StringComparer.Ordinal)),
            DepsJson: null
        );

        var executable = new RawExecutableData("myapp", 100, 1, IsManagedDotnet: false);

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "dotnet-app",
            dotnet: dotnet,
            node: null,
            typeScript: null,
            staticSite: null,
            executable: executable,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.ShouldContain(p => p.Type == "dotnet-runtime");
        results.ShouldNotContain(p => p.Type == "executable");
    }

    [Fact]
    public void Curate_ExecutableAppTypeWithDotnetEvidence_EmitsOnlyExecutablePanelWithNudge()
    {
        // Bill ruling 2: soft nudge, single panel, NOT side-by-side.
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig("net10.0", [], [], new Dictionary<string, JsonElement>(StringComparer.Ordinal)),
            DepsJson: null
        );

        var executable = new RawExecutableData("myapp.exe", 1024, 1, IsManagedDotnet: true);

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "executable",
            dotnet: dotnet,
            node: null,
            typeScript: null,
            staticSite: null,
            executable: executable,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("executable");

        var data = results[0].Data.ShouldBeOfType<ExecutableData>();

        data.IsManagedDotnet.ShouldBeTrue();
        data.BinaryName.ShouldBe("myapp.exe");
    }

    [Fact]
    public void Curate_ExecutableAppTypeNonDotnet_StillEmitsOnlyExecutablePanel()
    {
        var executable = new RawExecutableData("myapp", 512, 1, IsManagedDotnet: false);

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "executable",
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: null,
            executable: executable,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("executable");
    }

    [Fact]
    public void Curate_LegacyOverloadWithoutAppType_PreservesPreviousBehavior()
    {
        // The pre-card-#220 callers (some tests) use the original 5-arg
        // overload. They must continue to extract everything for any matching
        // raw data.
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig("net10.0", [], [], new Dictionary<string, JsonElement>(StringComparer.Ordinal)),
            DepsJson: null
        );

        var results = ProbeCurator.Curate(dotnet, null, null, null, "/fake/dir");

        results.ShouldContain(p => p.Type == "dotnet-runtime");
    }
}
