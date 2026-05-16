using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Registry;
using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Covers #316 / Axis B -- the platform grants each hosted published
// dotnet-app a sandbox-writable runtime working directory by construction so a
// relative Directory.CreateDirectory("./data/") lands on a writable filesystem
// instead of the read-only artifact location under the hardened system-scope
// unit. Three seams, mirroring the CH-C (#313) test shape:
//
//  - HostedAppWorkingDirectory : per-app path composition + lazy create + reap
//  - HostedDotnetWorkingDirectoryRedirect.ShouldRedirect : the gate
//    (dotnet-app + published shape + operator has not pinned a working dir)
//  - HostedDotnetWorkingDirectoryRedirect.Redirect : moves the launched cwd
//    while keeping the artifact location as the dll/runtimeconfig discovery
//    anchor (the bare dll arg is absolutized -- the recon §(g) load-bearing
//    constraint)
public class HostedAppWorkingDirectoryTests
{
    // ---- HostedAppWorkingDirectory: path / create / reap (CH-C twin) --------

    [Fact]
    public void ResolvePath_ComposesPerAppPathUnderDataRootAppCwd()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "collabhost-cwd-resolve");

        var resolved = HostedAppWorkingDirectory.ResolvePath(dataRoot, "collaboard");

        resolved.ShouldBe(Path.Combine(dataRoot, "app-cwd", "collaboard"));
    }

    [Fact]
    public void EnsureFor_CreatesPerAppDirectory_AndReturnsItsPath()
    {
        var dataRoot = CreateScratchDataRoot();

        try
        {
            var directory = new HostedAppWorkingDirectory(dataRoot, NullLogger<HostedAppWorkingDirectory>.Instance);

            var path = directory.EnsureFor("api-app");

            path.ShouldBe(Path.Combine(dataRoot, "app-cwd", "api-app"));
            Directory.Exists(path).ShouldBeTrue();
        }
        finally
        {
            Cleanup(dataRoot);
        }
    }

    // The end-to-end Axis-B assertion at the unit level: an app handed this
    // working dir as cwd can create a relative ./data/ under it -- the exact
    // operation that fails with EROFS when cwd is the read-only artifact dir.
    [Fact]
    public void RelativeDataDirectory_UnderTheProvisionedCwd_IsWritable()
    {
        var dataRoot = CreateScratchDataRoot();

        try
        {
            var directory = new HostedAppWorkingDirectory(dataRoot, NullLogger<HostedAppWorkingDirectory>.Instance);

            var cwd = directory.EnsureFor("collaboard");

            // Resolve "./data/collaboard.db" the way the hosted child would --
            // against its cwd -- and prove the create succeeds.
            var relativeDataDir = Path.Combine(cwd, "data");
            Directory.CreateDirectory(relativeDataDir);
            File.WriteAllText(Path.Combine(relativeDataDir, "collaboard.db"), "stub");

            File.Exists(Path.Combine(relativeDataDir, "collaboard.db")).ShouldBeTrue();
        }
        finally
        {
            Cleanup(dataRoot);
        }
    }

    [Fact]
    public void Reap_RemovesThePerAppDirectoryTree()
    {
        var dataRoot = CreateScratchDataRoot();

        try
        {
            var directory = new HostedAppWorkingDirectory(dataRoot, NullLogger<HostedAppWorkingDirectory>.Instance);

            var path = directory.EnsureFor("doomed-app");
            File.WriteAllText(Path.Combine(path, "state.db"), "stub");

            directory.Reap("doomed-app");

            Directory.Exists(path).ShouldBeFalse();
        }
        finally
        {
            Cleanup(dataRoot);
        }
    }

    [Fact]
    public void Reap_OnNeverProvisionedSlug_DoesNotThrow()
    {
        var dataRoot = CreateScratchDataRoot();

        try
        {
            var directory = new HostedAppWorkingDirectory(dataRoot, NullLogger<HostedAppWorkingDirectory>.Instance);

            Should.NotThrow(() => directory.Reap("never-existed"));
        }
        finally
        {
            Cleanup(dataRoot);
        }
    }

    // ---- ShouldRedirect: the gate -------------------------------------------

    [Fact]
    public void ShouldRedirect_PublishedDotnetAppWithoutOperatorWorkingDir_RedirectsTrue()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetRuntimeConfiguration
        };

        var redirect = HostedDotnetWorkingDirectoryRedirect.ShouldRedirect
        (
            "dotnet-app",
            configuration,
            out var operatorPinned
        );

        redirect.ShouldBeTrue();
        operatorPinned.ShouldBeFalse();
    }

    // Operator-pinned working directory is an explicit "run with cwd = this"
    // instruction -- it wins silently, symmetric to CH-C's operator escape
    // hatch and CLAUDE.md Repository Rule 3 (no regression for anything an
    // operator configured today).
    [Fact]
    public void ShouldRedirect_OperatorPinnedWorkingDirectory_DoesNotRedirect()
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetRuntimeConfiguration,
            WorkingDirectory = "subdir"
        };

        var redirect = HostedDotnetWorkingDirectoryRedirect.ShouldRedirect
        (
            "dotnet-app",
            configuration,
            out var operatorPinned
        );

        redirect.ShouldBeFalse();
        operatorPinned.ShouldBeTrue();
    }

    // Non-published dotnet-app discovery shapes are out of scope: the redirect
    // only cleanly absolutizes the single bare-dll argument of the published
    // shape. DotNetProject / Manual keep today's behavior (cwd = artifact dir)
    // -- no regression, just not extended.
    [Theory]
    [InlineData(DiscoveryStrategy.DotNetProject)]
    [InlineData(DiscoveryStrategy.PackageJson)]
    [InlineData(DiscoveryStrategy.Manual)]
    public void ShouldRedirect_NonPublishedDiscoveryStrategy_DoesNotRedirect(DiscoveryStrategy strategy)
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = strategy
        };

        var redirect = HostedDotnetWorkingDirectoryRedirect.ShouldRedirect
        (
            "dotnet-app",
            configuration,
            out _
        );

        redirect.ShouldBeFalse();
    }

    [Theory]
    [InlineData("nodejs-app")]
    [InlineData("static-site")]
    [InlineData("executable")]
    [InlineData("system-service")]
    public void ShouldRedirect_NonDotnetAppType_DoesNotRedirect(string appTypeSlug)
    {
        var configuration = new ProcessConfiguration
        {
            DiscoveryStrategy = DiscoveryStrategy.DotNetRuntimeConfiguration
        };

        var redirect = HostedDotnetWorkingDirectoryRedirect.ShouldRedirect
        (
            appTypeSlug,
            configuration,
            out _
        );

        redirect.ShouldBeFalse();
    }

    // ---- Redirect: cwd moves, discovery anchor preserved --------------------

    // The recon §(g) load-bearing assertion: DiscoverDotNetApplication returns
    // a BARE dll filename resolved by `dotnet` against cwd. After the redirect
    // the cwd is the writable per-app dir, so the dll argument MUST be absolute
    // and anchored at the (still-artifact) discovery location -- otherwise
    // `dotnet` can no longer find the dll or *.runtimeconfig.json.
    [Fact]
    public void Redirect_AbsolutizesBareDllAgainstArtifactDir_AndMovesCwd()
    {
        var artifactDir = Path.Combine(Path.GetTempPath(), "collabhost-artifact-anchor");
        var writableCwd = Path.Combine(Path.GetTempPath(), "collabhost-writable-cwd");

        var discovered = new DiscoveredProcess("dotnet", "Collaboard.Api.dll", artifactDir);

        var redirected = HostedDotnetWorkingDirectoryRedirect.Redirect(discovered, writableCwd);

        redirected.Command.ShouldBe("dotnet");
        redirected.WorkingDirectory.ShouldBe(writableCwd);
        // dll arg now absolute AND still anchored at the artifact dir (the
        // discovery anchor) -- not at the new cwd.
        redirected.Arguments.ShouldBe(Path.GetFullPath(Path.Combine(artifactDir, "Collaboard.Api.dll")));
    }

    // A provider-augmented argument suffix (e.g. the proxy port flag) must be
    // preserved verbatim -- only the leading dll token is absolutized.
    [Fact]
    public void Redirect_PreservesAugmentedArgumentSuffix()
    {
        var artifactDir = Path.Combine(Path.GetTempPath(), "collabhost-artifact-suffix");
        var writableCwd = Path.Combine(Path.GetTempPath(), "collabhost-writable-suffix");

        var discovered = new DiscoveredProcess("dotnet", "Collaboard.Api.dll --urls http://localhost:5005", artifactDir);

        var redirected = HostedDotnetWorkingDirectoryRedirect.Redirect(discovered, writableCwd);

        var absoluteDll = Path.GetFullPath(Path.Combine(artifactDir, "Collaboard.Api.dll"));
        redirected.Arguments.ShouldBe(absoluteDll + " --urls http://localhost:5005");
        redirected.WorkingDirectory.ShouldBe(writableCwd);
    }

    private static string CreateScratchDataRoot()
    {
        var root = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-cwd-tests",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(root);

        return root;
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Scratch cleanup is best-effort -- a leaked temp dir does not fail the test.
        }
    }
}
