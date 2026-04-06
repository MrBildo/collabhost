namespace Collabhost.Api.Probes;

// JSON-serialized DTOs -- List<T> is practical for curation output
#pragma warning disable MA0016

public static class ProbeCurator
{
    public static List<ProbeEntry> Curate
    (
        RawDotnetData? dotnet,
        RawNodeData? node,
        RawTypeScriptData? typeScript,
        string? projectRoot,
        string artifactDirectory
    )
    {
        var results = new List<ProbeEntry>();

        if (dotnet is not null)
        {
            CurateDotnet(dotnet, results);
        }

        if (node?.PackageJson is not null)
        {
            CurateNode(node, results);
            CurateReact(node.PackageJson, projectRoot, artifactDirectory, results);
        }

        if (typeScript is not null)
        {
            CurateTypeScript(typeScript, results);
        }

        return results;
    }

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
        var searchDirectory = ResolveSearchDirectory(projectRoot, artifactDirectory);

        var (bundler, bundlerVersion) = DetectBundler(allDeps);
        var router = DetectRouter(allDeps);
        var stateManagement = DetectStateManagement(allDeps);
        var cssStrategy = DetectCssStrategy(allDeps, searchDirectory);

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
        string? searchDirectory
    )
    {
        // Check for Tailwind via dependency or config file
        if (allDeps.ContainsKey("tailwindcss") || allDeps.ContainsKey("@tailwindcss/vite"))
        {
            return "tailwind";
        }

        if (searchDirectory is not null && HasConfigFile(searchDirectory, "tailwind.config.*"))
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
        return searchDirectory is not null && HasConfigFile(searchDirectory, "postcss.config.*") ? "postcss" : null;
    }

    private static string? ResolveSearchDirectory(string? projectRoot, string artifactDirectory) =>
        !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
            ? projectRoot
            : Directory.Exists(artifactDirectory) ? artifactDirectory : null;

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
