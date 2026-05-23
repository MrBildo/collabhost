using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

using Collabhost.Api.Supervisor;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #329 -- the CH-C seam guard.
//
// #325 narrowed HostedDotnetBundleEnvironment.ShouldProvision with a
// selfExtracts discriminator computed through ArtifactSelfExtracts ->
// ArtifactEvidenceCollector.Collect(...).Signals (SingleFileBinary). The gate
// is correct today and closes the S26 production mis-fire. But its correctness
// silently rests on ArtifactEvidenceCollector's first-match rule order -- a
// file #325 does not touch but now depends on for a production-safety
// provisioning decision. A future reorder of that collector, or a
// self-contained single-file publish that ships a stray root
// *.runtimeconfig.json, would silently re-arm the original failure class with
// no error and no failing test.
//
// HostedAppBundleDirectoryTests proves ShouldProvision and ArtifactSelfExtracts
// at the unit level using STUB files (a 4-byte ".exe" + 4-byte ".pdb" trip the
// pdb-pair signal). That test does not exercise the full collector contract --
// neither the magic-bytes bundle-signature path that real single-file
// publishes ship with, nor the "first match wins" rule ordering against a
// genuine framework-dependent publish layout.
//
// This file closes that gap. It publishes a real self-contained single-file
// .NET artifact AND a real framework-dependent loose-*.dll publish at test
// setup, then asserts ShouldProvision lands the right answer when fed through
// the real ArtifactEvidenceCollector (no mocked signal anywhere). The tests
// guard the S26 contract specifically: the positive test catches reorders that
// demote single-file detection below a rule that fires on a console single-file
// layout, and the negative test catches weakening of the runtimeconfig
// short-circuit. Reorders that preserve the semantics for these two specific
// layouts would not be caught.
//
// Fixture model: test-time `dotnet publish` against a minimal generated csproj
// (one Program.cs main entrypoint). Rationale documented in PR body. Cost:
// ~one-time publish per scenario per test run. Cached via IAsyncLifetime so
// every test in the class shares the same artifact pair.
public class ArtifactEvidenceCollectorSeamTests : IAsyncLifetime
{
    private string _scratchRoot = string.Empty;
    private string _selfContainedSingleFileDir = string.Empty;
    private string _frameworkDependentDir = string.Empty;
    private bool _publishSucceeded;
    private string _publishFailureReason = string.Empty;

    public async Task InitializeAsync()
    {
        _scratchRoot = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-ch-c-seam",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_scratchRoot);

        try
        {
            var projectDir = await BuildFixtureProjectAsync();

            // Default debug-symbol shape -- we want the publish that mirrors what
            // an operator running `dotnet publish -r <rid> --self-contained
            // -p:PublishSingleFile=true` actually deploys, which includes the
            // sibling .pdb. The .pdb-next-to-binary pair is the cheapest cross-
            // platform single-file signal the collector emits (TryDetectPdbPair),
            // so this configuration exercises the production-default path through
            // ArtifactEvidenceCollector. The same publish also embeds the legacy
            // bundle signature -- defense in depth on the same contract -- though
            // see the PR body for a K-class note on the bundle-signature reader
            // against the current .NET single-file format.
            _selfContainedSingleFileDir = await PublishAsync
            (
                projectDir,
                outputName: "self-contained-single-file",
                args:
                [
                    "publish",
                    "-c", "Release",
                    "-r", RuntimeInformation.RuntimeIdentifier,
                    "--self-contained", "true",
                    "-p:PublishSingleFile=true",
                    "-p:IncludeNativeLibrariesForSelfExtract=true",
                    "-p:GenerateDocumentationFile=false",
                    "--nologo"
                ]
            );

            _frameworkDependentDir = await PublishAsync
            (
                projectDir,
                outputName: "framework-dependent",
                args:
                [
                    "publish",
                    "-c", "Release",
                    "--self-contained", "false",
                    "-p:PublishSingleFile=false",
                    "-p:UseAppHost=false",
                    "-p:DebugType=none",
                    "-p:GenerateDocumentationFile=false",
                    "--nologo"
                ]
            );

            _publishSucceeded = true;
        }
        catch (PublishFailedException ex)
        {
            _publishFailureReason = ex.Message;
        }
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_scratchRoot))
            {
                Directory.Delete(_scratchRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Scratch cleanup is best-effort -- a leaked temp dir under
            // GetTempPath() does not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort rationale as the IOException branch above.
        }

        return Task.CompletedTask;
    }

    // The cross-file contract guard, positive side. A real self-contained
    // single-file publish carries the 16-byte bundle signature at the tail of
    // its apphost binary -- ArtifactEvidenceCollector's first-match rule order
    // is supposed to fall through *.runtimeconfig.json (none at root in a
    // single-file publish) and *.csproj (none at all) into the single-file
    // detection branch, which scans candidate binaries via
    // SingleFileBundleReader.TryRead and emits SingleFileBinary. If a future
    // edit reorders rules so something else pre-empts that path, ArtifactSelf-
    // Extracts returns false here and ShouldProvision flips to false on a real
    // self-extracting artifact -- which is the S24/S26 production failure
    // class. This test breaks loudly when that happens.
    [SkippableFact]
    public void SelfContainedSingleFile_ProvisionsTrue_ThroughTheRealCollector()
    {
        SkipIfPublishUnavailable();

        var selfExtracts = HostedDotnetBundleEnvironment.ArtifactSelfExtracts(_selfContainedSingleFileDir);

        selfExtracts.ShouldBeTrue
        (
            $"Real self-contained single-file publish at '{_selfContainedSingleFileDir}' "
            + "did NOT register as self-extracting via the real ArtifactEvidenceCollector. "
            + "If ArtifactEvidenceCollector.CollectDotnet was reordered so that an earlier "
            + "rule pre-empts single-file detection, or SingleFileBundleReader stopped "
            + "recognising the bundle signature, this is the production-safety regression "
            + "#325 closed."
        );

        var shouldProvision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            [],
            selfExtracts,
            out var operatorPinned
        );

        shouldProvision.ShouldBeTrue();
        operatorPinned.ShouldBeFalse();
    }

    // The cross-file contract guard, negative side. A framework-dependent
    // loose-*.dll publish ships a root *.runtimeconfig.json -- the collector's
    // FIRST rule (runtimeConfigs.Length > 0) must match and short-circuit
    // before single-file detection is even considered. This is exactly the
    // S26 shape: a non-self-extracting app must NOT receive a fabricated
    // DOTNET_BUNDLE_EXTRACT_BASE_DIR. If a future edit weakens the
    // runtimeconfig short-circuit so it falls through into single-file
    // detection by accident, this test catches it.
    [SkippableFact]
    public void FrameworkDependentLooseDll_DoesNotProvision_ThroughTheRealCollector()
    {
        SkipIfPublishUnavailable();

        // Sanity: the published layout actually matches the S26 shape we mean
        // to assert against -- root *.runtimeconfig.json plus loose *.dll, no
        // bundled apphost. If this fails the publish flags above changed shape
        // and the rest of the test no longer asserts what its comment claims.
        Directory.GetFiles(_frameworkDependentDir, "*.runtimeconfig.json").ShouldNotBeEmpty
        (
            "framework-dependent publish output should carry a root *.runtimeconfig.json"
        );

        Directory.GetFiles(_frameworkDependentDir, "*.dll").ShouldNotBeEmpty
        (
            "framework-dependent publish output should carry loose *.dll files"
        );

        var selfExtracts = HostedDotnetBundleEnvironment.ArtifactSelfExtracts(_frameworkDependentDir);

        selfExtracts.ShouldBeFalse
        (
            $"Framework-dependent publish at '{_frameworkDependentDir}' was misclassified "
            + "as self-extracting. This is the S26 production failure class: the original "
            + "two-clause provision gate fabricated an unused bundle dir and injected an "
            + "inert env var for non-self-extracting apps, contributing to three production "
            + "rollbacks (#322). If a future edit to ArtifactEvidenceCollector.CollectDotnet "
            + "weakens the *.runtimeconfig.json short-circuit, this test catches it."
        );

        var shouldProvision = HostedDotnetBundleEnvironment.ShouldProvision
        (
            "dotnet-app",
            [],
            selfExtracts,
            out var operatorPinned
        );

        shouldProvision.ShouldBeFalse();
        operatorPinned.ShouldBeFalse();
    }

    private void SkipIfPublishUnavailable()
    {
        if (_publishSucceeded)
        {
            return;
        }

        // The fixture build/publish failed at IAsyncLifetime.InitializeAsync.
        // Surface a soft skip rather than a flaky failure: the test guards a
        // real cross-file contract and an SDK / network availability issue is
        // not the contract regression we want to flag. CI has the SDK on PATH,
        // and a developer on an offline machine without the runtime pack still
        // exercises every other test in the suite.
        throw new SkipException
        (
            "ArtifactEvidenceCollectorSeamTests requires a working `dotnet publish` "
            + "against the local SDK to build the real-artifact fixtures. The fixture "
            + $"build failed: {_publishFailureReason}"
        );
    }

    private async Task<string> BuildFixtureProjectAsync()
    {
        var projectDir = Path.Combine(_scratchRoot, "fixture");

        Directory.CreateDirectory(projectDir);

        // Minimum-viable .NET executable -- enough for `dotnet publish` to emit
        // a real apphost, .runtimeconfig.json and (under --self-contained
        // PublishSingleFile=true) the embedded bundle signature.
        var csproj = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>Collabhost.Tests.Fixtures.HostedAppProbe</RootNamespace>
                <AssemblyName>collabhost-fixture-hosted-app</AssemblyName>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """;

        var program = """
            // Minimal entrypoint -- the fixture exists only to be published.
            // It is never executed; its publish output is the artifact under test.
            Console.WriteLine("collabhost-fixture-hosted-app");
            """;

        await File.WriteAllTextAsync
        (
            Path.Combine(projectDir, "collabhost-fixture-hosted-app.csproj"),
            csproj
        );

        await File.WriteAllTextAsync
        (
            Path.Combine(projectDir, "Program.cs"),
            program
        );

        return projectDir;
    }

    private async Task<string> PublishAsync(string projectDir, string outputName, string[] args)
    {
        var outputDir = Path.Combine(_scratchRoot, "publish", outputName);

        Directory.CreateDirectory(outputDir);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDir);

        using var process = Process.Start(startInfo)
            ?? throw new PublishFailedException("Process.Start('dotnet ...') returned null.");

        // Capture in-memory to surface useful diagnostics when the publish
        // fails. We don't expect publishes to be enormous; the fixture project
        // is one Program.cs.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // 5-minute ceiling -- way over expected (~20-60s) but well under any
        // sensible CI step timeout. A hung publish is itself a CI failure
        // worth surfacing.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited between WaitForExitAsync and Kill --
                // nothing to clean up.
            }

            throw new PublishFailedException
            (
                $"`dotnet {string.Join(' ', args)}` timed out after 5 minutes."
            );
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return process.ExitCode != 0
            ? throw new PublishFailedException
            (
                $"`dotnet {string.Join(' ', args)}` exited {process.ExitCode.ToString(CultureInfo.InvariantCulture)}."
                + Environment.NewLine
                + "STDOUT:" + Environment.NewLine + stdout
                + Environment.NewLine
                + "STDERR:" + Environment.NewLine + stderr
            )
            : outputDir;
    }

    // S3871 wants exception types public, but this one is a test-only signal
    // for the IAsyncLifetime publish step -- it never escapes the assembly.
    // Suppressed via #pragma narrowly so the rule still catches genuine cases.
#pragma warning disable S3871
    private sealed class PublishFailedException(string message) : Exception(message);
#pragma warning restore S3871
}
