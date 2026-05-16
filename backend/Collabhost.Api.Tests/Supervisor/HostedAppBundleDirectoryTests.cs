using System.Collections.Frozen;

using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Covers #313 / CH-C -- the platform grants each hosted single-file dotnet-app a
// sandbox-writable bundle-extraction dir by construction. Three seams:
//
//  - HostedAppBundleDirectory  : per-app path composition + lazy create + reap
//  - HostedDotnetBundleEnvironment : the should-provision / operator-wins decision
//  - MergeEnvironmentVariables : proves an operator override still wins through
//                                the real merge pipeline (the §(b) ordering claim)
public class HostedAppBundleDirectoryTests
{
    [Fact]
    public void ResolvePath_ComposesPerAppPathUnderDataRootAppBundles()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), "collabhost-bundle-resolve");

        var resolved = HostedAppBundleDirectory.ResolvePath(dataRoot, "collaboard");

        resolved.ShouldBe(Path.Combine(dataRoot, "app-bundles", "collaboard"));
    }

    [Fact]
    public void EnsureFor_CreatesPerAppDirectory_AndReturnsItsPath()
    {
        var dataRoot = CreateScratchDataRoot();

        try
        {
            var directory = new HostedAppBundleDirectory(dataRoot, NullLogger<HostedAppBundleDirectory>.Instance);

            var path = directory.EnsureFor("api-app");

            path.ShouldBe(Path.Combine(dataRoot, "app-bundles", "api-app"));
            Directory.Exists(path).ShouldBeTrue();
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
            var directory = new HostedAppBundleDirectory(dataRoot, NullLogger<HostedAppBundleDirectory>.Instance);

            var path = directory.EnsureFor("doomed-app");
            File.WriteAllText(Path.Combine(path, "extracted-native.so"), "stub");

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
            var directory = new HostedAppBundleDirectory(dataRoot, NullLogger<HostedAppBundleDirectory>.Instance);

            Should.NotThrow(() => directory.Reap("never-existed"));
        }
        finally
        {
            Cleanup(dataRoot);
        }
    }

    [Fact]
    public void ShouldProvision_DotnetAppWithoutOperatorOverride_ProvisionsTrue()
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            OverrideKeys(),
            out var operatorPinned
        );

        provision.ShouldBeTrue();
        operatorPinned.ShouldBeFalse();
    }

    [Fact]
    public void ShouldProvision_DotnetAppWithOperatorPinnedVariable_DoesNotProvision()
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            OverrideKeys("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            out var operatorPinned
        );

        provision.ShouldBeFalse();
        operatorPinned.ShouldBeTrue();
    }

    [Theory]
    [InlineData("nodejs-app")]
    [InlineData("static-site")]
    [InlineData("executable")]
    [InlineData("system-service")]
    public void ShouldProvision_NonDotnetAppType_DoesNotProvision(string appTypeSlug)
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            appTypeSlug,
            OverrideKeys(),
            out _
        );

        provision.ShouldBeFalse();
    }

    [Fact]
    public void OperatorOverride_SurvivesTheMergePipeline()
    {
        // The supervisor injects the provisioned path into the capability-variables
        // tier ONLY when ShouldProvision is true. When the operator pinned the var,
        // ShouldProvision is false, so the resolved environment-defaults value (the
        // operator override) flows through MergeEnvironmentVariables untouched --
        // no provider in this scenario contributes the key. This pins the §(b)
        // "operator escape hatch preserved" ordering claim end-to-end.
        var operatorValue = "/operator/chosen/bundle/dir";

        var capabilityVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = operatorValue
        };

        var overrideKeys = OverrideKeys("DOTNET_BUNDLE_EXTRACT_BASE_DIR");

        var shouldProvision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            overrideKeys,
            out _
        );

        shouldProvision.ShouldBeFalse();

        var merged = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            [],
            "some-app",
            NullLogger<ProcessSupervisor>.Instance
        );

        merged["DOTNET_BUNDLE_EXTRACT_BASE_DIR"].ShouldBe(operatorValue);
    }

    // Builds an operator-override key set without tripping CA1861 (inline array
    // literals passed to a method called across tests). Mirrors the helper shape
    // in ProcessSupervisorEnvironmentMergeTests.
    private static FrozenSet<string> OverrideKeys(params string[] keys) =>
        keys.ToFrozenSet(StringComparer.Ordinal);

    private static string CreateScratchDataRoot()
    {
        var root = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-bundle-tests",
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
