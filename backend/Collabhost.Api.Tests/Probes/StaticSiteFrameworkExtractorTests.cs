using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #359 -- structural fingerprint extractor for static-site built output.
// Coverage matrix: React + Vite-bundled (Collaboard portal shape), Next.js
// (_next/ directory), Astro (_astro/ directory), SvelteKit (_app/ directory),
// generic meta-generator (Hugo), and the negative-guard plain-HTML case.
public class StaticSiteFrameworkExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public StaticSiteFrameworkExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-static-framework-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Extract_NonExistentDirectory_ReturnsNull()
    {
        var result = StaticSiteFrameworkExtractor.Extract(Path.Combine(_tempDir, "missing"));

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_EmptyDirectory_ReturnsNull()
    {
        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_ReactViteBundle_DetectsReactAndVite()
    {
        // The Collaboard portal shape: <div id="root"> + content-hashed assets
        // + ESM module script tag. This is what `vite build` of a React app
        // ships, and what Theo's card body is centrally about.
        File.WriteAllText
        (
            Path.Combine(_tempDir, "index.html"),
            """
            <!DOCTYPE html>
            <html lang="en">
              <head>
                <meta charset="UTF-8" />
                <script type="module" crossorigin src="/assets/index-Bx7yLp_q.js"></script>
                <link rel="stylesheet" crossorigin href="/assets/index-Bb9SoF2H.css">
              </head>
              <body>
                <div id="root"></div>
              </body>
            </html>
            """
        );

        Directory.CreateDirectory(Path.Combine(_tempDir, "assets"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "index-Bx7yLp_q.js"), "/* bundled */");
        File.WriteAllText(Path.Combine(_tempDir, "assets", "index-Bb9SoF2H.css"), "/* bundled */");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("react");
        result.BuildTool.ShouldBe("vite");
        result.MetaFramework.ShouldBeNull();
        result.Confidence.ShouldBe("medium");
        result.EvidenceSource.ShouldContain("root-element");
    }

    [Fact]
    public void Extract_NextJsBuildOutput_DetectsNextHighConfidence()
    {
        // _next/ is the canonical Next.js static export marker. Its presence
        // alone is high-confidence evidence -- nothing else writes _next/ as
        // a top-level directory.
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body><div id=\"__next\"></div></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "_next"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "_next", "static"));
        File.WriteAllText(Path.Combine(_tempDir, "_next", "static", "chunk-abc.js"), "");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("react");
        result.MetaFramework.ShouldBe("next");
        result.BuildTool.ShouldBe("webpack");
        result.Confidence.ShouldBe("high");
        result.EvidenceSource.ShouldBe("_next-directory");
    }

    [Fact]
    public void Extract_AstroBuildOutput_DetectsAstroHighConfidence()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body><h1>Astro site</h1></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "_astro"));
        File.WriteAllText(Path.Combine(_tempDir, "_astro", "hoisted.abc123.js"), "");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("astro");
        result.MetaFramework.ShouldBe("astro");
        result.BuildTool.ShouldBe("vite");
        result.Confidence.ShouldBe("high");
        result.EvidenceSource.ShouldBe("_astro-directory");
    }

    [Fact]
    public void Extract_SvelteKitBuildOutput_DetectsSvelteKitHighConfidence()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "_app"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "_app", "immutable"));
        File.WriteAllText(Path.Combine(_tempDir, "_app", "version.json"), "{\"version\":\"1\"}");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("svelte");
        result.MetaFramework.ShouldBe("sveltekit");
        result.BuildTool.ShouldBe("vite");
        result.Confidence.ShouldBe("high");
        result.EvidenceSource.ShouldBe("_app-directory");
    }

    [Fact]
    public void Extract_NuxtBuildOutput_DetectsNuxtHighConfidence()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body><div id=\"__nuxt\"></div></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "_nuxt"));

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("vue");
        result.MetaFramework.ShouldBe("nuxt");
        result.BuildTool.ShouldBe("vite");
        result.Confidence.ShouldBe("high");
        result.EvidenceSource.ShouldBe("_nuxt-directory");
    }

    [Fact]
    public void Extract_HugoGenerator_DetectsStaticOnlyViaMetaTag()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "index.html"),
            """
            <!DOCTYPE html>
            <html>
              <head>
                <meta name="generator" content="Hugo 0.135.0">
                <title>My Blog</title>
              </head>
              <body>
                <h1>Welcome</h1>
              </body>
            </html>
            """
        );

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("static-only");
        result.MetaFramework.ShouldBeNull();
        result.BuildTool.ShouldBe("unbundled");
        result.Confidence.ShouldBe("high");
        result.EvidenceSource.ShouldContain("Hugo");
    }

    [Fact]
    public void Extract_UnrecognizedGenerator_SurfacesValueVerbatim()
    {
        // The generator string is surfaced regardless of whether we recognize it,
        // so the operator can see what the site reported. Framework falls back to
        // "unknown" when neither the generator nor any other fingerprint matches.
        File.WriteAllText
        (
            Path.Combine(_tempDir, "index.html"),
            "<html><head><meta name=\"generator\" content=\"SomeNewSSG 2.0\"></head><body></body></html>"
        );

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("unknown");
        result.EvidenceSource.ShouldContain("SomeNewSSG 2.0");
    }

    [Fact]
    public void Extract_VueAppShell_DetectsVueMediumConfidence()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "index.html"),
            "<html><body><div id=\"app\"></div><script type=\"module\" src=\"/assets/main-XyZ12345.js\"></script></body></html>"
        );
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "main-XyZ12345.js"), "");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("vue");
        result.BuildTool.ShouldBe("vite");
        result.Confidence.ShouldBe("medium");
        result.EvidenceSource.ShouldContain("root-element");
    }

    [Fact]
    public void Extract_PlainStaticSite_ReturnsStaticOnlyUnbundled()
    {
        // Negative-guard: a hand-authored HTML page with no framework shell,
        // no generator meta tag, no recognized assets pattern. The right
        // answer is "static-only / unbundled" with low confidence -- not
        // a false positive on framework.
        File.WriteAllText
        (
            Path.Combine(_tempDir, "index.html"),
            """
            <!DOCTYPE html>
            <html>
              <head><title>My Page</title></head>
              <body>
                <h1>Hello</h1>
                <p>Hand-written HTML, no framework.</p>
              </body>
            </html>
            """
        );

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("static-only");
        result.MetaFramework.ShouldBeNull();
        result.BuildTool.ShouldBe("unbundled");
        result.Confidence.ShouldBe("low");
        result.EvidenceSource.ShouldBe("no-framework-fingerprint");
    }

    [Fact]
    public void Extract_NoIndexHtmlNoMetaFrameworkDir_ReturnsNull()
    {
        // No index.html and no _next/_astro/_app/_nuxt -- there is no static
        // site here to fingerprint. The generic StaticSiteExtractor will also
        // return null; the framework extractor matches that disposition.
        Directory.CreateDirectory(Path.Combine(_tempDir, "some-other-dir"));

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_MetaFrameworkDirectoryDominatesRootElement()
    {
        // When _next/ is present AND <div id="root"> is in index.html, the
        // _next/ signal wins -- it is the more specific (and higher-confidence)
        // marker. This protects against React-via-Next being misidentified as
        // generic React.
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body><div id=\"root\"></div></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "_next"));

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.MetaFramework.ShouldBe("next");
        result.Framework.ShouldBe("react");
        result.Confidence.ShouldBe("high");
    }

    [Fact]
    public void Extract_WebpackRuntimeChunk_DetectsWebpackBuildTool()
    {
        // Older webpack builds emit `runtime~main.<hash>.js` -- distinctive
        // enough to be a single-signal match for the build tool, even when
        // the framework is detected via a root-element selector.
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html><body><div id=\"root\"></div></body></html>");
        Directory.CreateDirectory(Path.Combine(_tempDir, "assets"));
        File.WriteAllText(Path.Combine(_tempDir, "assets", "runtime~main.7a9b3.js"), "");
        File.WriteAllText(Path.Combine(_tempDir, "assets", "main.7a9b3.js"), "");

        var result = StaticSiteFrameworkExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.Framework.ShouldBe("react");
        result.BuildTool.ShouldBe("webpack");
    }
}
