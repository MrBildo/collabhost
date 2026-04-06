using System.Text.Json;

using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class ProbeCuratorTests
{
    [Fact]
    public void Curate_AllNull_ReturnsEmptyList()
    {
        var results = ProbeCurator.Curate(null, null, null, null, "/fake/dir");

        results.ShouldBeEmpty();
    }

    [Fact]
    public void Curate_DotnetRuntime_ProducesTwoEntries()
    {
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig
            (
                "net10.0",
                [
                    new RawFrameworkReference("Microsoft.NETCore.App", "10.0.0"),
                    new RawFrameworkReference("Microsoft.AspNetCore.App", "10.0.0")
                ],
                [],
                new Dictionary<string, JsonElement>
(StringComparer.Ordinal)
                {
                    ["System.GC.Server"] = JsonDocument.Parse("true").RootElement
                }
            ),
            new RawDepsJson
            (
                ".NETCoreApp,Version=v10.0",
                new Dictionary<string, RawDepsLibrary>
(StringComparer.Ordinal)
                {
                    ["Microsoft.EntityFrameworkCore/10.0.0"] = new("package", "10.0.0"),
                    ["Shouldly/4.2.1"] = new("package", "4.2.1"),
                    ["MyApp.Shared/1.0.0"] = new("project", "1.0.0")
                }
            )
        );

        var results = ProbeCurator.Curate(dotnet, null, null, null, "/fake/dir");

        results.Count.ShouldBe(2);

        results[0].Type.ShouldBe("dotnet-runtime");
        results[0].Label.ShouldBe(".NET Runtime");

        var runtime = results[0].Data.ShouldBeOfType<DotnetRuntimeData>();

        runtime.Tfm.ShouldBe("net10.0");
        runtime.RuntimeVersion.ShouldBe("10.0.0");
        runtime.IsAspNetCore.ShouldBeTrue();
        runtime.IsSelfContained.ShouldBeFalse();
        runtime.ServerGc.ShouldBeTrue();

        results[1].Type.ShouldBe("dotnet-dependencies");

        var deps = results[1].Data.ShouldBeOfType<DotnetDependenciesData>();

        deps.PackageCount.ShouldBe(2);
        deps.ProjectReferenceCount.ShouldBe(1);
        deps.Notable.ShouldContain(n => n.Name == "EF Core");
    }

    [Fact]
    public void Curate_SelfContained_DetectsCorrectly()
    {
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig
            (
                "net10.0",
                [],
                [
                    new RawFrameworkReference("Microsoft.NETCore.App", "10.0.5"),
                    new RawFrameworkReference("Microsoft.AspNetCore.App", "10.0.5")
                ],
                []
            ),
            null
        );

        var results = ProbeCurator.Curate(dotnet, null, null, null, "/fake/dir");

        results.Count.ShouldBe(1);

        var runtime = results[0].Data.ShouldBeOfType<DotnetRuntimeData>();

        runtime.IsSelfContained.ShouldBeTrue();
        runtime.RuntimeVersion.ShouldBe("10.0.5");
        runtime.IsAspNetCore.ShouldBeTrue();
    }

    [Fact]
    public void Curate_NodePackageJson_ProducesNodeEntry()
    {
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "my-app", "2.0.0", "module", ">=22.0.0", "pnpm@9.15.4",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["express"] = "4.18.0" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["typescript"] = "5.8.3" }
            ),
            "pnpm"
        );

        var results = ProbeCurator.Curate(null, node, null, null, "/fake/dir");

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("node");
        results[0].Label.ShouldBe("Node.js");

        var data = results[0].Data.ShouldBeOfType<NodeData>();

        data.EngineVersion.ShouldBe(">=22.0.0");
        data.PackageManager.ShouldBe("pnpm");
        data.PackageManagerVersion.ShouldBe("9.15.4");
        data.ModuleSystem.ShouldBe("esm");
        data.DependencyCount.ShouldBe(1);
        data.DevDependencyCount.ShouldBe(1);
    }

    [Fact]
    public void Curate_CommonJsModuleSystem_WhenTypeAbsent()
    {
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "test", null, null, null, null,
                [],
                []
            ),
            null
        );

        var results = ProbeCurator.Curate(null, node, null, null, "/fake/dir");

        var data = results[0].Data.ShouldBeOfType<NodeData>();

        data.ModuleSystem.ShouldBe("commonjs");
    }

    [Fact]
    public void Curate_ReactDetection_FromDependencies()
    {
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "my-app", null, "module", null, null,
                new Dictionary<string, string>
(StringComparer.Ordinal)
                {
                    ["react"] = "^19.1.0",
                    ["react-dom"] = "^19.1.0",
                    ["react-router-dom"] = "^7.0.0",
                    ["@tanstack/react-query"] = "^5.0.0"
                },
                new Dictionary<string, string>
(StringComparer.Ordinal)
                {
                    ["vite"] = "6.4.1",
                    ["typescript"] = "5.8.3"
                }
            ),
            null
        );

        var results = ProbeCurator.Curate(null, node, null, null, "/fake/dir");

        // Should produce node + react entries
        results.Count.ShouldBe(2);
        results[0].Type.ShouldBe("node");
        results[1].Type.ShouldBe("react");

        var react = results[1].Data.ShouldBeOfType<ReactData>();

        react.Version.ShouldBe("^19.1.0");
        react.Bundler.ShouldBe("vite");
        react.BundlerVersion.ShouldBe("6.4.1");
        react.Router.ShouldBe("react-router");
        react.StateManagement.ShouldBe("tanstack-query");
    }

    [Fact]
    public void Curate_NoReact_SkipsReactEntry()
    {
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "express-app", null, null, null, null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["express"] = "4.18.0" },
                []
            ),
            null
        );

        var results = ProbeCurator.Curate(null, node, null, null, "/fake/dir");

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("node");
    }

    [Fact]
    public void Curate_TypeScript_ProducesEntry()
    {
        var ts = new RawTypeScriptData
        (
            "5.8.3",
            new RawTsConfig(true, "ES2022", "ESNext")
        );

        var results = ProbeCurator.Curate(null, null, ts, null, "/fake/dir");

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("typescript");
        results[0].Label.ShouldBe("TypeScript");

        var data = results[0].Data.ShouldBeOfType<TypeScriptData>();

        data.Version.ShouldBe("5.8.3");
        data.Strict.ShouldBeTrue();
        data.Target.ShouldBe("ES2022");
        data.Module.ShouldBe("ESNext");
    }

    [Fact]
    public void Curate_DisplayOrder_RuntimeBeforeDepsBeforeNodeBeforeReactBeforeTypeScript()
    {
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig
            (
                "net10.0",
                [new RawFrameworkReference("Microsoft.NETCore.App", "10.0.0")],
                [],
                []
            ),
            new RawDepsJson(null, [])
        );

        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "test", null, null, null, null,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["react"] = "19.0.0" },
                []
            ),
            null
        );

        var ts = new RawTypeScriptData("5.8.3", new RawTsConfig(true, null, null));

        var results = ProbeCurator.Curate(dotnet, node, ts, null, "/fake/dir");

        results.Count.ShouldBe(5);
        results[0].Type.ShouldBe("dotnet-runtime");
        results[1].Type.ShouldBe("dotnet-dependencies");
        results[2].Type.ShouldBe("node");
        results[3].Type.ShouldBe("react");
        results[4].Type.ShouldBe("typescript");
    }

    [Fact]
    public void Curate_NotablePackages_CuratesHumanReadableNames()
    {
        var dotnet = new RawDotnetData
        (
            new RawRuntimeConfig("net10.0", [], [], []),
            new RawDepsJson
            (
                null,
                new Dictionary<string, RawDepsLibrary>
(StringComparer.Ordinal)
                {
                    ["Microsoft.EntityFrameworkCore.Sqlite/10.0.0"] = new("package", "10.0.0"),
                    ["OpenTelemetry.Extensions.Hosting/1.12.0"] = new("package", "1.12.0"),
                    ["Polly.Core/8.2.0"] = new("package", "8.2.0"),
                    ["SomeUnknownPackage/1.0.0"] = new("package", "1.0.0"),
                }
            )
        );

        var results = ProbeCurator.Curate(dotnet, null, null, null, "/fake/dir");

        var deps = results[1].Data.ShouldBeOfType<DotnetDependenciesData>();

        deps.Notable.ShouldContain(n => n.Name == "SQLite");
        deps.Notable.ShouldContain(n => n.Name == "OpenTelemetry");
        deps.Notable.ShouldContain(n => n.Name == "Polly");
        deps.Notable.ShouldNotContain(n => n.Name == "SomeUnknownPackage");
    }

    [Fact]
    public void Curate_PackageManagerFromLockfile_WhenNoPackageManagerField()
    {
        var node = new RawNodeData
        (
            new RawPackageJson
            (
                "test", null, null, null, null,
                [],
                []
            ),
            "yarn"
        );

        var results = ProbeCurator.Curate(null, node, null, null, "/fake/dir");

        var data = results[0].Data.ShouldBeOfType<NodeData>();

        data.PackageManager.ShouldBe("yarn");
        data.PackageManagerVersion.ShouldBeNull();
    }
}
