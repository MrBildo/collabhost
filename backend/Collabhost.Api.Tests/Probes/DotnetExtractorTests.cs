using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class DotnetExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public DotnetExtractorTests()
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
    public void Extract_NoRuntimeConfig_ReturnsNull()
    {
        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_EmptyDirectory_ReturnsNull()
    {
        var result = DotnetExtractor.Extract(Path.Combine(_tempDir, "nonexistent"));

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_FrameworkDependent_ParsesRuntimeConfig()
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
                "configProperties": {
                  "System.GC.Server": true,
                  "System.Runtime.TieredCompilation": true
                }
              }
            }
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");
        result.RuntimeConfig.Frameworks.Count.ShouldBe(2);
        result.RuntimeConfig.Frameworks[0].Name.ShouldBe("Microsoft.NETCore.App");
        result.RuntimeConfig.IncludedFrameworks.ShouldBeEmpty();
        result.RuntimeConfig.ConfigProperties.ShouldContainKey("System.GC.Server");
    }

    [Fact]
    public void Extract_SelfContained_ParsesIncludedFrameworks()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "includedFrameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.5" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.5" }
                ]
              }
            }
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Frameworks.ShouldBeEmpty();
        result.RuntimeConfig.IncludedFrameworks.Count.ShouldBe(2);
        result.RuntimeConfig.IncludedFrameworks[0].Version.ShouldBe("10.0.5");
    }

    [Fact]
    public void Extract_WithDepsJson_ParsesLibraries()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.runtimeconfig.json"),
            """{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}"""
        );

        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.deps.json"),
            """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "libraries": {
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" },
                "Shouldly/4.2.1": { "type": "package" },
                "MyApp.Shared/1.0.0": { "type": "project" }
              }
            }
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.DepsJson.ShouldNotBeNull();
        result.DepsJson.Libraries.Count.ShouldBe(3);
        result.DepsJson.Libraries["Microsoft.EntityFrameworkCore/10.0.0"].Type.ShouldBe("package");
        result.DepsJson.Libraries["Microsoft.EntityFrameworkCore/10.0.0"].Version.ShouldBe("10.0.0");
        result.DepsJson.Libraries["MyApp.Shared/1.0.0"].Type.ShouldBe("project");
    }

    [Fact]
    public void Extract_MalformedJson_ReturnsNullRuntimeConfig()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.runtimeconfig.json"),
            "not valid json {"
        );

        var result = DotnetExtractor.Extract(_tempDir);

        // File exists but is unparseable -- RuntimeConfig is null
        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldBeNull();
    }

    [Fact]
    public void Extract_LegacySingleFramework_ParsesCorrectly()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.runtimeconfig.json"),
            """
            {
              "runtimeOptions": {
                "tfm": "netcoreapp2.1",
                "framework": {
                  "name": "Microsoft.NETCore.App",
                  "version": "2.1.0"
                }
              }
            }
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("netcoreapp2.1");
        result.RuntimeConfig.Frameworks.Count.ShouldBe(1);
        result.RuntimeConfig.Frameworks[0].Name.ShouldBe("Microsoft.NETCore.App");
        result.RuntimeConfig.Frameworks[0].Version.ShouldBe("2.1.0");
    }

    [Fact]
    public void Extract_ProjectDirectory_FindsRuntimeConfigInBuildOutput()
    {
        // Simulate a .NET project directory with .csproj and build output
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
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

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");
        result.RuntimeConfig.Frameworks.Count.ShouldBe(2);
    }

    [Fact]
    public void Extract_ProjectDirectory_FallsBackToCsprojTfm()
    {
        // Simulate a .NET project directory that has never been built
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");
        result.RuntimeConfig.Frameworks.ShouldBeEmpty();
        result.DepsJson.ShouldBeNull();
    }

    [Fact]
    public void Extract_ProjectDirectory_MultiTargeting_TakesFirstTfm()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyLib.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net8.0;netstandard2.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");
    }

    [Fact]
    public void Extract_ProjectDirectory_BuildOutputDepsJson_Parsed()
    {
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
            """{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}"""
        );

        File.WriteAllText
        (
            Path.Combine(buildDir, "MyApp.deps.json"),
            """
            {
              "runtimeTarget": { "name": ".NETCoreApp,Version=v10.0" },
              "libraries": {
                "Microsoft.EntityFrameworkCore/10.0.0": { "type": "package" }
              }
            }
            """
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.DepsJson.ShouldNotBeNull();
        result.DepsJson.Libraries.Count.ShouldBe(1);
    }

    [Fact]
    public void Extract_ProjectDirectory_PrefersReleaseBuild_WhenNoDebug()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "MyApp.csproj"),
            """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"""
        );

        var releaseDir = Path.Combine(_tempDir, "bin", "Release", "net10.0");
        Directory.CreateDirectory(releaseDir);

        File.WriteAllText
        (
            Path.Combine(releaseDir, "MyApp.runtimeconfig.json"),
            """{"runtimeOptions":{"tfm":"net10.0","frameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}"""
        );

        var result = DotnetExtractor.Extract(_tempDir);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");
    }
}
