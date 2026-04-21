using System.Diagnostics;
using System.Reflection;

using Collabhost.Api.Platform;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class VersionFlagTests
{
    [Fact]
    public async Task VersionFlag_ExitsZeroWithCorrectStdout()
    {
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { assemblyPath!, "--version" },
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);

        process.ShouldNotBeNull();

        var stdout = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        process.ExitCode.ShouldBe(0);
        stdout.Trim().ShouldBe($"Collabhost {VersionInfo.Current}");
    }

    [Fact]
    public async Task VersionFlagShortForm_ExitsZeroWithCorrectStdout()
    {
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { assemblyPath!, "-v" },
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi);

        process.ShouldNotBeNull();

        var stdout = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        process.ExitCode.ShouldBe(0);
        stdout.Trim().ShouldBe($"Collabhost {VersionInfo.Current}");
    }
}
