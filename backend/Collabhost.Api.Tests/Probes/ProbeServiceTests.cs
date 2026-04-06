using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class ProbeServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ProbeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RunProbesForDirectory_EmptyDir_ReturnsEmpty()
    {
        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void RunProbesForDirectory_DotnetApp_ProducesDotnetEntries()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
                ],
                "configProperties": { "System.GC.Server": true }
              }
            }
            """
        );

        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.deps.json"),
            """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "libraries": {
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              }
            }
            """
        );

        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.Count.ShouldBe(2);
        results[0].Type.ShouldBe("dotnet-runtime");
        results[1].Type.ShouldBe("dotnet-dependencies");
    }

    [Fact]
    public void RunProbesForDirectory_ReactProject_ProducesNodeReactTypeScript()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """
            {
              "name": "test-app",
              "type": "module",
              "dependencies": { "react": "^19.1.0", "react-dom": "^19.1.0" },
              "devDependencies": { "typescript": "5.8.3", "vite": "6.4.1" }
            }
            """
        );

        File.WriteAllText
        (
            Path.Combine(_tempDir, "tsconfig.json"),
            """{"compilerOptions":{"strict":true,"target":"ES2022","module":"ESNext"}}"""
        );

        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.Count.ShouldBe(3);
        results[0].Type.ShouldBe("node");
        results[1].Type.ShouldBe("react");
        results[2].Type.ShouldBe("typescript");
    }

    [Fact]
    public void RunProbesForDirectory_ProjectRoot_SeparateFromArtifact()
    {
        var projectRoot = Path.Combine(_tempDir, "source");
        var artifactDir = Path.Combine(_tempDir, "dist");

        Directory.CreateDirectory(projectRoot);
        Directory.CreateDirectory(artifactDir);

        File.WriteAllText
        (
            Path.Combine(projectRoot, "package.json"),
            """
            {
              "name": "from-root",
              "type": "module",
              "dependencies": { "react": "^19.0.0" },
              "devDependencies": { "typescript": "5.8.3" }
            }
            """
        );

        File.WriteAllText
        (
            Path.Combine(projectRoot, "tsconfig.json"),
            """{"compilerOptions":{"strict":true}}"""
        );

        var results = ProbeService.RunProbesForDirectory(artifactDir, projectRoot);

        results.Count.ShouldBe(3);
        results[0].Type.ShouldBe("node");

        var nodeData = results[0].Data.ShouldBeOfType<NodeData>();

        nodeData.DependencyCount.ShouldBe(1);

        results[1].Type.ShouldBe("react");
        results[2].Type.ShouldBe("typescript");
    }

    [Fact]
    public void RunProbesForDirectory_NonexistentDir_ReturnsEmpty()
    {
        var results = ProbeService.RunProbesForDirectory
        (
            Path.Combine(_tempDir, "does-not-exist"),
            null
        );

        results.ShouldBeEmpty();
    }

    [Fact]
    public void RunProbesForDirectory_DotnetProject_ProbesFromBuildOutput()
    {
        // Simulate a .NET project directory (DotNetProject discovery strategy)
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"""
        );

        var buildDir = Path.Combine(_tempDir, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(buildDir);

        File.WriteAllText
        (
            Path.Combine(buildDir, "MyApp.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "frameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
                ]
              }
            }
            """
        );

        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("dotnet-runtime");

        var runtime = results[0].Data.ShouldBeOfType<DotnetRuntimeData>();

        runtime.Tfm.ShouldBe("net10.0");
        runtime.IsAspNetCore.ShouldBeTrue();
    }

    [Fact]
    public void RunProbesForDirectory_DotnetProject_FallsBackToCsprojTfm()
    {
        // .NET project directory that has never been built
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"""
        );

        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.Count.ShouldBe(1);
        results[0].Type.ShouldBe("dotnet-runtime");

        var runtime = results[0].Data.ShouldBeOfType<DotnetRuntimeData>();

        runtime.Tfm.ShouldBe("net10.0");
    }

    [Fact]
    public void RunProbesForDirectory_StaticSiteDistDir_FindsPackageJsonInParent()
    {
        // Simulate a React static site: artifact dir is dist/, package.json is in parent
        var distDir = Path.Combine(_tempDir, "dist");
        Directory.CreateDirectory(distDir);

        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """
            {
              "name": "react-site",
              "type": "module",
              "dependencies": { "react": "^19.1.0", "react-dom": "^19.1.0" },
              "devDependencies": { "typescript": "5.8.3", "vite": "6.4.1" }
            }
            """
        );

        File.WriteAllText
        (
            Path.Combine(_tempDir, "tsconfig.json"),
            """{"compilerOptions":{"strict":true,"target":"ES2022","module":"ESNext"}}"""
        );

        // No projectRoot set -- should auto-detect via parent fallback
        var results = ProbeService.RunProbesForDirectory(distDir, null);

        results.Count.ShouldBe(3);
        results[0].Type.ShouldBe("node");
        results[1].Type.ShouldBe("react");
        results[2].Type.ShouldBe("typescript");

        var nodeData = results[0].Data.ShouldBeOfType<NodeData>();

        nodeData.DependencyCount.ShouldBe(2);
    }

    [Fact]
    public void RunProbesForDirectory_BareNodeApp_SuppressesEmptyPanel()
    {
        // A bare Node.js app with nothing meaningful should produce no results
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """{"name":"bare-server"}"""
        );

        File.WriteAllText
        (
            Path.Combine(_tempDir, "server.js"),
            """console.log("hello")"""
        );

        var results = ProbeService.RunProbesForDirectory(_tempDir, null);

        results.ShouldBeEmpty();
    }
}
