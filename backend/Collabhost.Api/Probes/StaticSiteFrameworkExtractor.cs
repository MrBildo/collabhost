namespace Collabhost.Api.Probes;

// Fingerprints the built output of a static-site artifact directory to identify
// the framework (React / Vue / Svelte / Astro / Next / Nuxt / SvelteKit /
// static-only) and the build tool (Vite / Webpack / Parcel / esbuild / unbundled).
//
// This is the symmetric complement to the dotnet-app extractors -- dotnet-app
// reads runtimeconfig.json and deps.json from the published artifact; static-site
// reads index.html + assets/ filenames + the presence of meta-framework output
// directories. No JS source is parsed; structural fingerprints only. Card #359.
//
// Detection priority (highest-signal first):
//   1. Meta-framework output directories (_next, _nuxt, _astro, _app) -- these are
//      single-purpose by convention. Their presence is near-certain evidence.
//   2. <meta name="generator"> tag in index.html -- explicit author declaration.
//   3. Root-element selector in index.html (<div id="root">, <div id="app">, etc.).
//   4. assets/ filename pattern (content-hashed *.js -- Vite/Rollup signature).
//   5. Webpack runtime chunk naming (runtime~main.<hash>.js).
//
// Returns null when the directory has no index.html AND no recognized framework
// output dirs -- there's no static site here to fingerprint.
public static class StaticSiteFrameworkExtractor
{
    // Read the first 64 KB of index.html only — even very heavy SPA shells fit in
    // that window, and a misconfigured 100 MB index.html should not pull the process apart.
    private const int _maxIndexHtmlBytesRead = 64 * 1024;

    // Meta-framework output directories. Each one is single-purpose by convention
    // -- the framework writes these as its build output and nothing else does.
    private static readonly (string Directory, string Framework, string MetaFramework, string BuildTool)[] _metaFrameworkDirectories =
    [
        ("_next", "react", "next", "webpack"),
        ("_nuxt", "vue", "nuxt", "vite"),
        ("_astro", "astro", "astro", "vite"),
        ("_app", "svelte", "sveltekit", "vite"),
    ];

    // Vite's default build output pattern is `assets/<name>-<hash>.js` where the
    // hash is 8+ Crockford-ish hex characters. Rollup and Vite share this shape.
    // Use ECMAScript regex with bounded backtracking.
    private static readonly Regex _viteAssetHashPattern = new
    (
        @"^.+-[A-Za-z0-9_]{8,}\.(js|css|mjs)$",
        RegexOptions.ECMAScript | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100)
    );

    // Webpack older builds emit `runtime~main.<hash>.js` plus `main.<hash>.js`.
    // The runtime~ prefix is distinctive enough to be a single-signal match.
    private static readonly Regex _webpackRuntimePattern = new
    (
        @"^runtime~.+\.js$",
        RegexOptions.ECMAScript | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100)
    );

    // ESM module script tag with `type="module"` attribute -- Vite always emits
    // this; webpack default builds emit classic scripts.
    private static readonly Regex _esmModuleScriptPattern = new
    (
        @"<script[^>]+type\s*=\s*[""']module[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100)
    );

    // <meta name="generator" content="..."> -- author-supplied identification.
    // ExplicitCapture means only the named content group is captured -- the
    // surrounding character classes don't materialize Group objects.
    private static readonly Regex _generatorMetaPattern = new
    (
        @"<meta[^>]+name\s*=\s*[""']generator[""'][^>]+content\s*=\s*[""'](?<content>[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100)
    );

    // Root-element selectors that frameworks use by strong convention.
    //   <div id="root">     React (Vite template, CRA modern)
    //   <div id="app">      Vue 2/3, classic Svelte
    //   <div id="__next">   Next.js (when not using App Router or SSG fallback)
    //   <astro-island>      Astro islands
    private static readonly (string Pattern, string Framework)[] _rootElementPatterns =
    [
        ("<div id=\"__next\"", "react"),
        ("<div id='__next'", "react"),
        ("<astro-island", "astro"),
        ("<div id=\"root\"", "react"),
        ("<div id='root'", "react"),
        ("<div id=\"app\"", "vue"),
        ("<div id='app'", "vue"),
    ];

    public static RawStaticSiteFrameworkData? Extract(string artifactDirectory)
    {
        if (!Directory.Exists(artifactDirectory))
        {
            return null;
        }

        // Highest-signal detection first: meta-framework output directories.
        // Their presence is essentially proof of the framework that emitted them.
        foreach (var (directory, framework, metaFramework, buildTool) in _metaFrameworkDirectories)
        {
            if (Directory.Exists(Path.Combine(artifactDirectory, directory)))
            {
                return new RawStaticSiteFrameworkData
                (
                    Framework: framework,
                    BuildTool: buildTool,
                    MetaFramework: metaFramework,
                    Confidence: "high",
                    EvidenceSource: $"{directory}-directory"
                );
            }
        }

        // Read index.html (best-effort, bounded). No index.html and no
        // meta-framework dir means there is nothing for us to fingerprint --
        // either it isn't a SPA-shaped static site, or the structure is so
        // unconventional we can't speak to it. Return null; the generic
        // StaticSiteExtractor will still emit its shape probe.
        var indexHtmlPath = Path.Combine(artifactDirectory, "index.html");
        var indexHtml = ReadIndexHtmlBounded(indexHtmlPath);

        if (indexHtml is null)
        {
            return null;
        }

        // Author-declared generator tag is a strong signal (Hugo, Jekyll, 11ty,
        // Docusaurus, VitePress all emit this). Surface verbatim so the operator
        // can see what the site reported, even when we don't recognize the value.
        var generatorMatch = _generatorMetaPattern.Match(indexHtml);

        if (generatorMatch.Success)
        {
            var generator = generatorMatch.Groups["content"].Value.Trim();
            var (framework, metaFramework) = MapGeneratorValue(generator);
            var buildTool = DetectBuildToolFromAssets(artifactDirectory, indexHtml);

            return new RawStaticSiteFrameworkData
            (
                Framework: framework ?? "unknown",
                BuildTool: buildTool,
                MetaFramework: metaFramework,
                Confidence: "high",
                EvidenceSource: $"meta-generator:{generator}"
            );
        }

        // Root-element fingerprint. First match wins; the order in
        // _rootElementPatterns puts the most-specific (`__next`, `astro-island`)
        // ahead of the generic (`root`, `app`) so a Next.js shell that also
        // contains `<div id="root">` is identified as React-via-Next, not generic.
        foreach (var (pattern, framework) in _rootElementPatterns)
        {
            if (indexHtml.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                var buildTool = DetectBuildToolFromAssets(artifactDirectory, indexHtml);

                return new RawStaticSiteFrameworkData
                (
                    Framework: framework,
                    BuildTool: buildTool,
                    MetaFramework: null,
                    Confidence: "medium",
                    EvidenceSource: $"root-element:{pattern}"
                );
            }
        }

        // Negative guard: a plain static site (index.html + nothing recognizable)
        // returns the static-only / unbundled shape. This is genuine information
        // -- "we looked and there's no framework here" is the right answer for a
        // hand-authored HTML site or a Hugo-rendered site without the generator
        // meta tag.
        var fallbackBuildTool = DetectBuildToolFromAssets(artifactDirectory, indexHtml);

        return new RawStaticSiteFrameworkData
        (
            Framework: "static-only",
            BuildTool: fallbackBuildTool,
            MetaFramework: null,
            Confidence: "low",
            EvidenceSource: "no-framework-fingerprint"
        );
    }

    private static string? ReadIndexHtmlBounded(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream
            (
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );

            var buffer = new byte[_maxIndexHtmlBytesRead];
            var read = stream.Read(buffer, 0, buffer.Length);

            // UTF-8 with fallback -- HTML in the wild is almost always UTF-8,
            // but tolerate the occasional Latin-1 page by replacing invalid bytes.
            return System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string? Framework, string? MetaFramework) MapGeneratorValue(string generator)
    {
        // Match the generator string against well-known patterns. The string is
        // surfaced verbatim in EvidenceSource regardless of whether we recognize
        // it, so the operator can see what the site reported.
        if (generator.Contains("Astro", StringComparison.OrdinalIgnoreCase))
        {
            return ("astro", "astro");
        }

        if (generator.Contains("Next.js", StringComparison.OrdinalIgnoreCase))
        {
            return ("react", "next");
        }

        if (generator.Contains("Nuxt", StringComparison.OrdinalIgnoreCase))
        {
            return ("vue", "nuxt");
        }

        if (generator.Contains("Hugo", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("Jekyll", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("Eleventy", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("11ty", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("Docusaurus", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("VitePress", StringComparison.OrdinalIgnoreCase)
            || generator.Contains("MkDocs", StringComparison.OrdinalIgnoreCase))
        {
            // Static-site generators that emit HTML directly. The framework
            // category isn't "react/vue/svelte" -- it's a generator family.
            // Surface as static-only with the generator name carried in
            // EvidenceSource so the operator can read it.
            return ("static-only", null);
        }

        return (null, null);
    }

    private static string DetectBuildToolFromAssets(string artifactDirectory, string indexHtml)
    {
        // Vite signature: ESM module script tag + content-hashed assets in
        // `assets/`. Either signal alone is suggestive; together they're
        // near-certain.
        var hasEsmModule = _esmModuleScriptPattern.IsMatch(indexHtml);
        var (hasViteAssets, hasWebpackRuntime) = ScanAssetsDirectory(artifactDirectory);

        if (hasViteAssets && hasEsmModule)
        {
            return "vite";
        }

        if (hasWebpackRuntime)
        {
            return "webpack";
        }

        if (hasViteAssets)
        {
            // Content-hashed assets without ESM module tag -- could be Vite legacy
            // build, Rollup, or a similarly-configured webpack. Vite is the most
            // common producer of this pattern in 2026; treat as Vite with the
            // caveat captured in the surrounding confidence field.
            return "vite";
        }

        // No build-tool fingerprint at all -- this is genuine information for a
        // hand-authored static site (no bundler in the toolchain).
        return "unbundled";
    }

    private static (bool HasViteAssets, bool HasWebpackRuntime) ScanAssetsDirectory(string artifactDirectory)
    {
        var assetsDir = Path.Combine(artifactDirectory, "assets");

        if (!Directory.Exists(assetsDir))
        {
            return (false, false);
        }

        try
        {
            var files = Directory.EnumerateFiles(assetsDir, "*", SearchOption.TopDirectoryOnly);

            var hasViteAssets = false;
            var hasWebpackRuntime = false;

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);

                if (!hasWebpackRuntime && _webpackRuntimePattern.IsMatch(fileName))
                {
                    hasWebpackRuntime = true;
                }

                if (!hasViteAssets && _viteAssetHashPattern.IsMatch(fileName))
                {
                    hasViteAssets = true;
                }

                if (hasViteAssets && hasWebpackRuntime)
                {
                    break;
                }
            }

            return (hasViteAssets, hasWebpackRuntime);
        }
        catch (IOException)
        {
            return (false, false);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, false);
        }
    }
}
