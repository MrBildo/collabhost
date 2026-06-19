namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for curation output
#pragma warning disable MA0016

public static class ProbeCurator
{
    // Original signature retained for tests that don't care about AppType-aware
    // filtering. Equivalent to passing appTypeSlug=null which means "render
    // anything we have raw data for" (the pre-card-#220 behavior).
    public static List<ProbeEntry> Curate
    (
        RawDotnetData? dotnet,
        RawNodeData? node,
        RawTypeScriptData? typeScript,
        string? projectRoot,
        string artifactDirectory
    ) => Curate
    (
        appTypeSlug: null,
        dotnet,
        node,
        typeScript,
        staticSite: null,
        staticSiteFramework: null,
        executable: null,
        projectRoot,
        artifactDirectory
    );

    // Pre-#359 signature retained for tests that don't supply the static-site
    // framework data. Defers to the canonical 9-arg overload with null framework
    // data so the existing AppType-aware behavior is preserved.
    public static List<ProbeEntry> Curate
    (
        string? appTypeSlug,
        RawDotnetData? dotnet,
        RawNodeData? node,
        RawTypeScriptData? typeScript,
        RawStaticSiteData? staticSite,
        RawExecutableData? executable,
        string? projectRoot,
        string artifactDirectory
    ) => Curate
    (
        appTypeSlug,
        dotnet,
        node,
        typeScript,
        staticSite,
        staticSiteFramework: null,
        executable,
        projectRoot,
        artifactDirectory
    );

    public static List<ProbeEntry> Curate
    (
        string? appTypeSlug,
        RawDotnetData? dotnet,
        RawNodeData? node,
        RawTypeScriptData? typeScript,
        RawStaticSiteData? staticSite,
        RawStaticSiteFrameworkData? staticSiteFramework,
        RawExecutableData? executable,
        string? projectRoot,
        string artifactDirectory
    )
    {
        var results = new List<ProbeEntry>();

        // AppType-aware filter: emit only the panel families the operator's chosen
        // AppType cares about. Card #220 §3.5. When appTypeSlug is null we keep
        // the legacy "extract everything, render everything" behavior.
        var allowDotnet = appTypeSlug is null
            || string.Equals(appTypeSlug, "dotnet-app", StringComparison.Ordinal)
            || string.Equals(appTypeSlug, "executable", StringComparison.Ordinal);

        var allowNode = appTypeSlug is null
            || string.Equals(appTypeSlug, "nodejs-app", StringComparison.Ordinal);

        var allowStatic = appTypeSlug is null
            || string.Equals(appTypeSlug, "static-site", StringComparison.Ordinal);

        var allowExecutable = appTypeSlug is null
            || string.Equals(appTypeSlug, "executable", StringComparison.Ordinal);

        // Soft-nudge: when AppType is `executable` AND the directory looks like
        // .NET, emit ONLY the executable panel (with IsManagedDotnet=true so the
        // frontend renders the "consider re-registering" hint). Bill ruling 2:
        // single panel + nudge, NOT side-by-side.
        var executableLooksDotnet = string.Equals(appTypeSlug, "executable", StringComparison.Ordinal)
            && executable?.IsManagedDotnet == true;

        if (executableLooksDotnet)
        {
            CurateExecutable(executable!, results);

            return results;
        }

        if (allowDotnet && dotnet is not null
            && !string.Equals(appTypeSlug, "executable", StringComparison.Ordinal))
        {
            CurateDotnet(dotnet, results);
        }

        if (allowNode && node?.PackageJson is not null)
        {
            CurateNode(node, results);
            CurateReact(node.PackageJson, projectRoot, artifactDirectory, results);
        }

        if (allowNode && typeScript is not null)
        {
            CurateTypeScript(typeScript, results);
        }

        if (allowStatic && staticSite is not null)
        {
            CurateStaticSite(staticSite, results);
        }

        // Card #359 -- emit the framework panel for static-site apps when the
        // built-output fingerprint extractor produced a result. This is the
        // symmetric complement to the dotnet-runtime probe (both read shipped
        // artifacts). The nodejs-app React panel above stays package.json-keyed
        // and unchanged.
        if (allowStatic && staticSiteFramework is not null)
        {
            CurateStaticSiteFramework(staticSiteFramework, results);
        }

        if (allowExecutable && executable is not null)
        {
            CurateExecutable(executable, results);
        }

        return results;
    }

    private static void CurateStaticSiteFramework(RawStaticSiteFrameworkData data, List<ProbeEntry> results) =>
        results.Add
        (
            new ProbeEntry
            (
                "static-site-framework",
                "Framework",
                new StaticSiteFrameworkData
                (
                    data.Framework,
                    data.BuildTool,
                    data.MetaFramework,
                    data.Confidence,
                    data.EvidenceSource
                )
            )
        );

    private static void CurateStaticSite(RawStaticSiteData data, List<ProbeEntry> results) =>
        results.Add
        (
            new ProbeEntry
            (
                "static-site",
                "Static Site",
                new StaticSiteData
                (
                    data.HasIndexHtml,
                    data.HtmlFileCount,
                    data.TotalAssetBytes,
                    data.HasNestedAssets
                )
            )
        );

    private static void CurateExecutable(RawExecutableData data, List<ProbeEntry> results) =>
        results.Add
        (
            new ProbeEntry
            (
                "executable",
                "Executable",
                new ExecutableData
                (
                    data.BinaryName,
                    data.BinarySizeBytes,
                    data.CandidateBinaryCount,
                    data.IsManagedDotnet
                )
            )
        );

    private static void CurateDotnet(RawDotnetData dotnet, List<ProbeEntry> results)
    {
        if (dotnet.RuntimeConfig is not null)
        {
            CurateDotnetRuntime(dotnet.RuntimeConfig, results);
        }

        if (dotnet.DepsJson is not null)
        {
            CurateDotnetDependencies(dotnet.DepsJson, results);
        }
    }

    private static void CurateDotnetRuntime(RawRuntimeConfig config, List<ProbeEntry> results)
    {
        var tfm = config.Tfm ?? "unknown";

        var isSelfContained = config.IncludedFrameworks.Count > 0;

        var frameworkList = isSelfContained
            ? config.IncludedFrameworks
            : config.Frameworks;

        var runtimeVersion = frameworkList.Count > 0
            ? frameworkList[0].Version
            : "unknown";

        var isAspNetCore = frameworkList.Exists
        (
            f => string.Equals(f.Name, "Microsoft.AspNetCore.App", StringComparison.Ordinal)
        );

        var serverGc = config.ConfigProperties.TryGetValue("System.GC.Server", out var gcElement)
            && gcElement.ValueKind == JsonValueKind.True;

        results.Add
        (
            new ProbeEntry
            (
                "dotnet-runtime",
                ".NET Runtime",
                new DotnetRuntimeData(tfm, runtimeVersion, isAspNetCore, isSelfContained, serverGc)
            )
        );
    }

    private static void CurateDotnetDependencies(RawDepsJson depsJson, List<ProbeEntry> results)
    {
        var packageCount = 0;
        var projectReferenceCount = 0;

        foreach (var (_, library) in depsJson.Libraries)
        {
            if (string.Equals(library.Type, "package", StringComparison.OrdinalIgnoreCase))
            {
                packageCount++;
            }
            else if (string.Equals(library.Type, "project", StringComparison.OrdinalIgnoreCase))
            {
                projectReferenceCount++;
            }
        }

        var notable = DetectNotablePackages(depsJson.Libraries);

        results.Add
        (
            new ProbeEntry
            (
                "dotnet-dependencies",
                "Dependencies",
                new DotnetDependenciesData(packageCount, projectReferenceCount, notable)
            )
        );
    }

    private static List<NotableDependency> DetectNotablePackages
    (
        Dictionary<string, RawDepsLibrary> libraries
    )
    {
        var notable = new List<NotableDependency>();
        var detected = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, library) in libraries)
        {
            // Extract the package name from the "PackageName/Version" key format
            var packageName = key.Contains('/', StringComparison.Ordinal)
                ? key[..key.LastIndexOf('/')]
                : key;

            foreach (var (pattern, label, startsWith) in _notablePatterns)
            {
                if (detected.Contains(label))
                {
                    continue;
                }

                if (MatchesPattern(packageName, pattern, startsWith))
                {
                    detected.Add(label);

                    notable.Add(new NotableDependency(label, library.Version));
                }
            }
        }

        return notable;
    }

    private static void CurateNode(RawNodeData node, List<ProbeEntry> results)
    {
        var packageJson = node.PackageJson!;

        string? packageManager = null;
        string? packageManagerVersion = null;

        if (packageJson.PackageManager is not null)
        {
            var parts = packageJson.PackageManager.Split('@', 2);
            packageManager = parts[0];
            packageManagerVersion = parts.Length > 1 ? parts[1] : null;
        }
        else if (node.DetectedLockfile is not null)
        {
            packageManager = node.DetectedLockfile;
        }

        var moduleSystem = packageJson.Type switch
        {
            "module" => "esm",
            "commonjs" => "commonjs",
            null => "commonjs",
            "" => "commonjs",
            _ => "commonjs"
        };

        // Suppress the Node.js panel only for truly anonymous package.json files --
        // no name, no version, and nothing else meaningful. These are typically
        // incidental files found alongside non-Node projects.
        // If the package has a name or version, it's a real project and should
        // always show the panel, even if the data is sparse.
        var hasIdentity = packageJson.Name is not null || packageJson.Version is not null;
        var hasEngineVersion = packageJson.EngineNode is not null;
        var hasPackageManager = packageManager is not null;
        var hasDependencies = packageJson.Dependencies.Count > 0 || packageJson.DevDependencies.Count > 0;
        var hasNonDefaultModuleSystem = string.Equals(moduleSystem, "esm", StringComparison.Ordinal);

        if (!hasIdentity && !hasEngineVersion && !hasPackageManager && !hasDependencies && !hasNonDefaultModuleSystem)
        {
            return;
        }

        results.Add
        (
            new ProbeEntry
            (
                "node",
                "Node.js",
                new NodeData
                (
                    packageJson.EngineNode,
                    packageManager,
                    packageManagerVersion,
                    moduleSystem,
                    packageJson.Dependencies.Count,
                    packageJson.DevDependencies.Count
                )
            )
        );
    }

    private static void CurateReact
    (
        RawPackageJson packageJson,
        string? projectRoot,
        string artifactDirectory,
        List<ProbeEntry> results
    )
    {
        if (!packageJson.Dependencies.TryGetValue("react", out var reactVersion))
        {
            return;
        }

        var allDeps = MergeAllDependencies(packageJson);
        var searchDirectories = ResolveSearchDirectories(projectRoot, artifactDirectory);

        var (bundler, bundlerVersion) = DetectBundler(allDeps);
        var router = DetectRouter(allDeps);
        var stateManagement = DetectStateManagement(allDeps);
        var cssStrategy = DetectCssStrategy(allDeps, searchDirectories);

        results.Add
        (
            new ProbeEntry
            (
                "react",
                "React",
                new ReactData
                (
                    reactVersion,
                    bundler,
                    bundlerVersion,
                    router,
                    stateManagement,
                    cssStrategy
                )
            )
        );
    }

    private static void CurateTypeScript(RawTypeScriptData typeScript, List<ProbeEntry> results) =>
        results.Add
        (
            new ProbeEntry
            (
                "typescript",
                "TypeScript",
                new TypeScriptData
                (
                    typeScript.Version,
                    typeScript.TsConfig?.Strict ?? false,
                    typeScript.TsConfig?.Target,
                    typeScript.TsConfig?.Module
                )
            )
        );

    private static Dictionary<string, string> MergeAllDependencies(RawPackageJson packageJson)
    {
        var all = new Dictionary<string, string>(packageJson.Dependencies, StringComparer.Ordinal);

        foreach (var (key, value) in packageJson.DevDependencies)
        {
            all.TryAdd(key, value);
        }

        return all;
    }

    private static (string? Name, string? Version) DetectBundler(Dictionary<string, string> allDeps)
    {
        if (allDeps.TryGetValue("vite", out var viteVersion))
        {
            return ("vite", viteVersion);
        }

        if (allDeps.TryGetValue("webpack", out var webpackVersion))
        {
            return ("webpack", webpackVersion);
        }

        if (allDeps.TryGetValue("parcel", out var parcelVersion))
        {
            return ("parcel", parcelVersion);
        }

        if (allDeps.TryGetValue("esbuild", out var esbuildVersion))
        {
            return ("esbuild", esbuildVersion);
        }

        if (allDeps.TryGetValue("@rspack/core", out var rspackVersion))
        {
            return ("rspack", rspackVersion);
        }

        if (allDeps.TryGetValue("rollup", out var rollupVersion))
        {
            return ("rollup", rollupVersion);
        }

        return (null, null);
    }

    private static string? DetectRouter(Dictionary<string, string> allDeps) =>
        allDeps.ContainsKey("react-router") || allDeps.ContainsKey("react-router-dom")
            ? "react-router"
            : allDeps.ContainsKey("@tanstack/react-router")
                ? "tanstack-router"
                : allDeps.ContainsKey("next") ? "next" : null;

    private static string? DetectStateManagement(Dictionary<string, string> allDeps) =>
        allDeps.ContainsKey("@tanstack/react-query")
            ? "tanstack-query"
            : allDeps.ContainsKey("zustand")
                ? "zustand"
                : allDeps.ContainsKey("@reduxjs/toolkit") || allDeps.ContainsKey("redux")
                    ? "redux"
                    : allDeps.ContainsKey("jotai")
                        ? "jotai"
                        : allDeps.ContainsKey("recoil")
                            ? "recoil"
                            : allDeps.ContainsKey("mobx") ? "mobx" : null;

    private static string? DetectCssStrategy
    (
        Dictionary<string, string> allDeps,
        List<string> searchDirectories
    )
    {
        // Check for Tailwind via dependency or config file
        if (allDeps.ContainsKey("tailwindcss") || allDeps.ContainsKey("@tailwindcss/vite"))
        {
            return "tailwind";
        }

        if (HasConfigFileInAny(searchDirectories, "tailwind.config.*"))
        {
            return "tailwind";
        }

        if (allDeps.ContainsKey("styled-components"))
        {
            return "styled-components";
        }

        if (allDeps.ContainsKey("@emotion/react") || allDeps.ContainsKey("@emotion/styled"))
        {
            return "emotion";
        }

        if (allDeps.ContainsKey("@vanilla-extract/css"))
        {
            return "vanilla-extract";
        }

        // CSS modules are detected by config file presence, not a dependency
        return HasConfigFileInAny(searchDirectories, "postcss.config.*") ? "postcss" : null;
    }

    private static List<string> ResolveSearchDirectories(string? projectRoot, string artifactDirectory)
    {
        var directories = new List<string>();

        if (!string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot))
        {
            directories.Add(projectRoot);
        }

        if (Directory.Exists(artifactDirectory))
        {
            directories.Add(artifactDirectory);
        }

        return directories;
    }

    private static bool HasConfigFileInAny(List<string> directories, string pattern) =>
        directories.Exists(directory => HasConfigFile(directory, pattern));

    private static bool HasConfigFile(string directory, string pattern)
    {
        try
        {
            return Directory.GetFiles(directory, pattern).Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // Notable .NET package patterns for curation.
    // Order matters -- first match for a label wins.
    private static readonly (string Pattern, string Label, bool StartsWith)[] _notablePatterns =
    [
        ("Microsoft.EntityFrameworkCore", "EF Core", true),
        ("Microsoft.EntityFrameworkCore.Sqlite", "SQLite", false),
        ("Npgsql.EntityFrameworkCore.PostgreSQL", "PostgreSQL", false),
        ("Microsoft.EntityFrameworkCore.SqlServer", "SQL Server", false),
        ("Pomelo.EntityFrameworkCore.MySql", "MySQL", false),
        ("MongoDB.Driver", "MongoDB", false),
        ("OpenTelemetry", "OpenTelemetry", true),
        ("Polly", "Polly", true),
        ("Grpc.AspNetCore", "gRPC", true),
        ("Microsoft.AspNetCore.SignalR", "SignalR", true),
        ("MassTransit", "MassTransit", true),
        ("Serilog", "Serilog", true),
        ("NLog", "NLog", true),
        ("MediatR", "MediatR", false),
        ("FluentValidation", "FluentValidation", true),
        ("Dapper", "Dapper", false),
        ("StackExchange.Redis", "Redis", false),
        ("Hangfire", "Hangfire", true),
        ("Quartz", "Quartz.NET", true),
        ("Swashbuckle.AspNetCore", "Swagger", false),
        ("Microsoft.AspNetCore.Authentication.JwtBearer", "JWT Auth", false),
    ];

    private static bool MatchesPattern
    (
        string packageName,
        string pattern,
        bool startsWith
    ) =>
        startsWith
            ? packageName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
            : string.Equals(packageName, pattern, StringComparison.OrdinalIgnoreCase);
}
