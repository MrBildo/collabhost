using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #359 -- curator gating for the static-site-framework probe panel.
// The new probe must:
//   1. Emit for `static-site` apps when framework data is present.
//   2. NOT emit for `nodejs-app` (the existing React panel stays
//      package.json-keyed and unchanged).
//   3. NOT emit for `dotnet-app` / `executable` / `system-service`.
public class StaticSiteFrameworkCuratorTests
{
    [Fact]
    public void Curate_StaticSiteAppType_EmitsFrameworkPanel()
    {
        var framework = new RawStaticSiteFrameworkData
        (
            Framework: "react",
            BuildTool: "vite",
            MetaFramework: null,
            Confidence: "medium",
            EvidenceSource: "root-element:<div id=\"root\""
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "static-site",
            evidence: null,
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: null,
            staticSiteFramework: framework,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("static-site-framework");
        results[0].Label.ShouldBe("Framework");

        var data = results[0].Data.ShouldBeOfType<StaticSiteFrameworkData>();

        data.Framework.ShouldBe("react");
        data.BuildTool.ShouldBe("vite");
        data.MetaFramework.ShouldBeNull();
        data.Confidence.ShouldBe("medium");
    }

    [Fact]
    public void Curate_NodejsAppType_DoesNotEmitFrameworkPanel()
    {
        // The static-site framework probe is keyed to static-site only. nodejs-app
        // has its own package.json-keyed React panel that is unchanged by #359.
        var framework = new RawStaticSiteFrameworkData
        (
            Framework: "react",
            BuildTool: "vite",
            MetaFramework: null,
            Confidence: "medium",
            EvidenceSource: "root-element"
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "nodejs-app",
            evidence: null,
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: null,
            staticSiteFramework: framework,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.ShouldNotContain(p => p.Type == "static-site-framework");
    }

    [Fact]
    public void Curate_DotnetAppType_DoesNotEmitFrameworkPanel()
    {
        var framework = new RawStaticSiteFrameworkData
        (
            Framework: "react",
            BuildTool: "vite",
            MetaFramework: null,
            Confidence: "medium",
            EvidenceSource: "root-element"
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "dotnet-app",
            evidence: null,
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: null,
            staticSiteFramework: framework,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.ShouldNotContain(p => p.Type == "static-site-framework");
    }

    [Fact]
    public void Curate_StaticSiteAppType_EmitsBothShapeAndFrameworkPanels()
    {
        // The generic static-site shape probe and the new framework probe are
        // complementary -- both should surface for a static-site app, with the
        // shape probe coming first.
        var staticSite = new RawStaticSiteData
        (
            HasIndexHtml: true,
            HtmlFileCount: 1,
            TotalAssetBytes: 12345,
            HasNestedAssets: true
        );

        var framework = new RawStaticSiteFrameworkData
        (
            Framework: "react",
            BuildTool: "vite",
            MetaFramework: null,
            Confidence: "medium",
            EvidenceSource: "root-element"
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "static-site",
            evidence: null,
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: staticSite,
            staticSiteFramework: framework,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.Count.ShouldBe(2);
        results[0].Type.ShouldBe("static-site");
        results[1].Type.ShouldBe("static-site-framework");
    }

    [Fact]
    public void Curate_StaticSiteFrameworkDataNull_NoFrameworkPanelEmitted()
    {
        // The extractor returns null for an artifact directory with nothing to
        // fingerprint. The curator must NOT emit an empty framework panel in
        // that case.
        var staticSite = new RawStaticSiteData
        (
            HasIndexHtml: true,
            HtmlFileCount: 1,
            TotalAssetBytes: 100,
            HasNestedAssets: false
        );

        var results = ProbeCurator.Curate
        (
            appTypeSlug: "static-site",
            evidence: null,
            dotnet: null,
            node: null,
            typeScript: null,
            staticSite: staticSite,
            staticSiteFramework: null,
            executable: null,
            projectRoot: null,
            artifactDirectory: "/fake/dir"
        );

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("static-site");
    }
}
