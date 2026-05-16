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
    public void ShouldProvision_SelfExtractingDotnetAppWithoutOperatorOverride_ProvisionsTrue()
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            OverrideKeys(),
            selfExtracts: true,
            out var operatorPinned
        );

        provision.ShouldBeTrue();
        operatorPinned.ShouldBeFalse();
    }

    // The production mis-fire the narrowed gate closes (#322 decision 3): a
    // hosted dotnet-app whose artifact is a framework-dependent / non-single-
    // file publish does NO self-extraction at all. The original two-clause gate
    // fabricated an unused bundle dir + injected an inert env var for it (it did
    // exactly this in the only production install, a non-single-file v1.12.1
    // app). With the discriminator, a non-self-extracting dotnet-app does not
    // provision regardless of operator-override state.
    [Fact]
    public void ShouldProvision_NonSelfExtractingDotnetApp_DoesNotProvision()
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            OverrideKeys(),
            selfExtracts: false,
            out var operatorPinned
        );

        provision.ShouldBeFalse();
        operatorPinned.ShouldBeFalse();
    }

    [Fact]
    public void ShouldProvision_DotnetAppWithOperatorPinnedVariable_DoesNotProvision()
    {
        var provision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            OverrideKeys("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            selfExtracts: true,
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
            selfExtracts: true,
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
            selfExtracts: true,
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

    // ---- ArtifactSelfExtracts: the self-extraction discriminator ------------

    // A framework-dependent / non-single-file publish has a root
    // *.runtimeconfig.json beside loose *.dll -- ArtifactEvidenceCollector
    // returns a RuntimeConfig signal, NOT a SingleFileBinary signal. This is
    // the exact production mis-fire shape (v1.12.1): it does no self-extraction
    // and must not provision a bundle dir.
    [Fact]
    public void ArtifactSelfExtracts_NonSingleFilePublish_IsFalse()
    {
        var dir = CreateScratchDataRoot();

        try
        {
            File.WriteAllText(Path.Combine(dir, "Collaboard.Api.runtimeconfig.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "Collaboard.Api.dll"), "stub");

            HostedDotnetBundleEnvironment.ArtifactSelfExtracts(dir).ShouldBeFalse();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    // A self-contained single-file publish has NO root *.runtimeconfig.json and
    // NO *.csproj -- the collector falls through to single-file detection. The
    // cheapest cross-platform single-file signal is a pdb next to a same-stem
    // binary (TryDetectPdbPair). This shape DOES self-extract and must provision.
    [Fact]
    public void ArtifactSelfExtracts_SingleFilePublish_IsTrue()
    {
        var dir = CreateScratchDataRoot();

        try
        {
            File.WriteAllText(Path.Combine(dir, "Collaboard.Api.pdb"), "stub");
            File.WriteAllText(Path.Combine(dir, "Collaboard.Api.exe"), "stub");

            HostedDotnetBundleEnvironment.ArtifactSelfExtracts(dir).ShouldBeTrue();
        }
        finally
        {
            Cleanup(dir);
        }
    }

    [Fact]
    public void ArtifactSelfExtracts_NonexistentOrEmptyLocation_IsFalse()
    {
        HostedDotnetBundleEnvironment.ArtifactSelfExtracts("").ShouldBeFalse();
        HostedDotnetBundleEnvironment
            .ArtifactSelfExtracts(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
            .ShouldBeFalse();
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
