using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

public class DiscoveryStrategyTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine
    (
        Path.GetTempPath(),
        "collabhost-tests",
        Guid.NewGuid().ToString("N")
    );

    public DiscoveryStrategyTests() =>
        Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        Directory.Delete(_tempDirectory, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Discover_DotNetRuntimeConfiguration_FindsRuntimeConfig()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDirectory, "MyApp.runtimeconfig.json"),
            "{}"
        );

        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetRuntimeConfiguration
        };

        var result = DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory);

        result.Command.ShouldBe("dotnet");
        result.Arguments.ShouldBe("MyApp.dll");
        result.WorkingDirectory.ShouldBe(_tempDirectory);
    }

    [Fact]
    public void Discover_DotNetRuntimeConfiguration_NoRuntimeConfig_Throws()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetRuntimeConfiguration
        };

        Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        )
            .Message.ShouldContain("runtimeconfig.json");
    }

    [Fact]
    public void Discover_PackageJson_FindsStartScript()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDirectory, "package.json"),
            """{"scripts":{"start":"node server.js"}}"""
        );

        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.PackageJson
        };

        var result = DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory);

        result.Command.ShouldBe("npm");
        result.Arguments.ShouldBe("start");
        result.WorkingDirectory.ShouldBe(_tempDirectory);
    }

    [Fact]
    public void Discover_PackageJson_NoStartScript_Throws()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDirectory, "package.json"),
            """{"scripts":{"build":"tsc"}}"""
        );

        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.PackageJson
        };

        Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        )
            .Message.ShouldContain("scripts.start");
    }

    [Fact]
    public void Discover_PackageJson_NoPackageJson_Throws()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.PackageJson
        };

        Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        )
            .Message.ShouldContain("package.json");
    }

    [Fact]
    public void Discover_DotNetProject_FindsSingleCsproj()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDirectory, "MyApp.csproj"),
            "<Project />"
        );

        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetProject
        };

        var result = DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory);

        result.Command.ShouldBe("dotnet");
        result.Arguments.ShouldBe("run --project MyApp.csproj");
        result.WorkingDirectory.ShouldBe(_tempDirectory);
    }

    [Fact]
    public void Discover_DotNetProject_MultipleCsproj_Throws()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "App1.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(_tempDirectory, "App2.csproj"), "<Project />");

        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetProject
        };

        var exception = Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        );

        exception.Message.ShouldContain("Multiple");
        exception.Message.ShouldContain("App1.csproj");
        exception.Message.ShouldContain("App2.csproj");
    }

    [Fact]
    public void Discover_DotNetProject_NoCsproj_Throws()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetProject
        };

        Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        )
            .Message.ShouldContain(".csproj");
    }

    [Fact]
    public void Discover_Manual_UsesConfiguredValues()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.Manual,
            Command = "my-app",
            Arguments = "--verbose",
            WorkingDirectory = "/custom/path"
        };

        var result = DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory);

        result.Command.ShouldBe("my-app");
        result.Arguments.ShouldBe("--verbose");
        result.WorkingDirectory.ShouldBe("/custom/path");
    }

    [Fact]
    public void Discover_Manual_NoCommand_Throws()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.Manual,
            Command = null
        };

        Should.Throw<InvalidOperationException>
        (
            () => DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory)
        )
            .Message.ShouldContain("Command is required");
    }

    [Fact]
    public void Discover_Manual_NoWorkingDirectory_FallsBackToProvidedDirectory()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.Manual,
            Command = "my-app",
            WorkingDirectory = null
        };

        var result = DiscoveryStrategyExecutor.Discover(configuration, _tempDirectory);

        result.WorkingDirectory.ShouldBe(_tempDirectory);
    }
}
