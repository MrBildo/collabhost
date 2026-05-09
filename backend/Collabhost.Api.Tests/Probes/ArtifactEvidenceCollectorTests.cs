using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class ArtifactEvidenceCollectorTests : IDisposable
{
    private readonly string _tempDir;

    public ArtifactEvidenceCollectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }

        GC.SuppressFinalize(this);
    }

    // --- dotnet-app -----------------------------------------------------------

    [Fact]
    public void Collect_DotnetWithRuntimeConfig_FullMatchDotNetRuntimeConfiguration()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.runtimeconfig.json"), "{}");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.DotNetRuntimeConfiguration);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Dotnet);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.RuntimeConfig && s.Path == "MyApp.runtimeconfig.json");
    }

    [Fact]
    public void Collect_DotnetWithCsproj_FullMatchDotNetProject()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project />");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.DotNetProject);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Dotnet);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.ProjectFile);
    }

    [Fact]
    public void Collect_DotnetPrefersRuntimeConfigOverCsproj()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.runtimeconfig.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.csproj"), "<Project />");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.DotNetRuntimeConfiguration);
    }

    [Fact]
    public void Collect_DotnetSelfContainedPdbPair_FullMatchManualWithSingleFileSignals()
    {
        // pdb-next-to-binary is the cheapest signal; we don't have to fake an
        // executable bit because the helper accepts either an .exe (Windows) or
        // a bare-name binary (Linux) next to the pdb.
        File.WriteAllBytes(Path.Combine(_tempDir, "MyApp.exe"), [0x4d, 0x5a]); // MZ header bytes
        File.WriteAllBytes(Path.Combine(_tempDir, "MyApp.pdb"), [0x42, 0x53, 0x4a, 0x42]);

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Dotnet);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.SingleFileBinary);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.PdbPair);
    }

    [Fact]
    public void Collect_DotnetWwwrootOnly_LikelyMatchManualWithStaticAssetsSignal()
    {
        // No binary at root, no runtimeconfig, no csproj -- just wwwroot.
        Directory.CreateDirectory(Path.Combine(_tempDir, "wwwroot"));

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.LikelyMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.Wwwroot);
    }

    [Fact]
    public void Collect_DotnetEmpty_NotApplicableManual()
    {
        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "dotnet-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
        evidence.Signals.ShouldBeEmpty();
    }

    // --- nodejs-app -----------------------------------------------------------

    [Fact]
    public void Collect_NodejsWithStartScript_FullMatchPackageJson()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """{"scripts":{"start":"node index.js"}}"""
        );

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "nodejs-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.PackageJson);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Node);

        var signal = evidence.Signals.ShouldHaveSingleItem();

        signal.Kind.ShouldBe(EvidenceSignalKinds.PackageJson);
        signal.Attributes.ShouldNotBeNull();
        signal.Attributes!["hasStart"].ShouldBe("true");
    }

    [Fact]
    public void Collect_NodejsWithoutStartScript_LikelyMatchManual()
    {
        File.WriteAllText
        (
            Path.Combine(_tempDir, "package.json"),
            """{"scripts":{"build":"tsc"}}"""
        );

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "nodejs-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.LikelyMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);

        var signal = evidence.Signals.ShouldHaveSingleItem();

        signal.Attributes!["hasStart"].ShouldBe("false");
    }

    [Fact]
    public void Collect_NodejsWithoutPackageJson_NotApplicable()
    {
        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "nodejs-app");

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.Signals.ShouldBeEmpty();
    }

    // --- static-site ----------------------------------------------------------

    [Fact]
    public void Collect_StaticSiteIndexHtml_FullMatchNotApplicable()
    {
        File.WriteAllText(Path.Combine(_tempDir, "index.html"), "<html></html>");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "static-site");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.NotApplicable);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Static);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.IndexHtml && s.Path == "index.html");
    }

    [Fact]
    public void Collect_StaticSiteAlternateEntry_LikelyMatchNotApplicable()
    {
        File.WriteAllText(Path.Combine(_tempDir, "default.html"), "<html></html>");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "static-site");

        evidence.Fitness.ShouldBe(AppTypeFitness.LikelyMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.NotApplicable);
        evidence.Signals.ShouldContain(s => s.Path == "default.html");
    }

    [Fact]
    public void Collect_StaticSiteHtmlFilesOnly_LikelyMatchNotApplicable()
    {
        File.WriteAllText(Path.Combine(_tempDir, "page1.html"), "<html></html>");
        File.WriteAllText(Path.Combine(_tempDir, "page2.html"), "<html></html>");

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "static-site");

        evidence.Fitness.ShouldBe(AppTypeFitness.LikelyMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.NotApplicable);
        evidence.Signals.ShouldContain(s => s.Kind == EvidenceSignalKinds.HtmlFiles);
    }

    [Fact]
    public void Collect_StaticSiteEmpty_NotApplicable()
    {
        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "static-site");

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.NotApplicable);
        evidence.Signals.ShouldBeEmpty();
    }

    // --- executable -----------------------------------------------------------

    [Fact]
    public void Collect_ExecutableSingleExe_FullMatchManualWithBinarySignal()
    {
        File.WriteAllBytes(Path.Combine(_tempDir, "myapp.exe"), [0x4d, 0x5a]);

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "executable");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
        evidence.RuntimeFamily.ShouldBe(RuntimeFamilies.Executable);

        var signal = evidence.Signals.ShouldHaveSingleItem();

        signal.Kind.ShouldBe(EvidenceSignalKinds.BinaryAtRoot);
        signal.Attributes!["count"].ShouldBe("1");
        signal.Attributes!["binaryName"].ShouldBe("myapp.exe");
        signal.Attributes!["isManagedDotnet"].ShouldBe("false");
    }

    [Fact]
    public void Collect_ExecutableMultipleExes_LikelyMatchManual()
    {
        // Skip on Linux -- the rule there is "executable bit set" not "*.exe", so
        // dropping bare files won't produce candidates. Soak the Windows path here.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        File.WriteAllBytes(Path.Combine(_tempDir, "myapp.exe"), [0x4d, 0x5a]);
        File.WriteAllBytes(Path.Combine(_tempDir, "myapp-cli.exe"), [0x4d, 0x5a]);

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "executable");

        evidence.Fitness.ShouldBe(AppTypeFitness.LikelyMatch);
        evidence.Signals.ShouldHaveSingleItem();
        evidence.Signals[0].Attributes!["count"].ShouldBe("2");
    }

    [Fact]
    public void Collect_ExecutableEmpty_NotApplicable()
    {
        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "executable");

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.NotApplicable);
    }

    [Fact]
    public void Collect_ExecutableThatLooksLikeDotnet_SetsIsManagedDotnetTrue()
    {
        // pdb-pair next to the .exe is the cheapest "this is .NET" signal.
        File.WriteAllBytes(Path.Combine(_tempDir, "myapp.exe"), [0x4d, 0x5a]);
        File.WriteAllBytes(Path.Combine(_tempDir, "myapp.pdb"), [0x42, 0x53, 0x4a, 0x42]);

        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "executable");

        evidence.Fitness.ShouldBe(AppTypeFitness.FullMatch);
        evidence.Signals[0].Attributes!["isManagedDotnet"].ShouldBe("true");
    }

    // --- general --------------------------------------------------------------

    [Fact]
    public void Collect_NonExistentDirectory_NotApplicable()
    {
        var evidence = ArtifactEvidenceCollector.Collect
        (
            Path.Combine(_tempDir, "does-not-exist"),
            "dotnet-app"
        );

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
    }

    [Fact]
    public void Collect_UnknownAppType_NotApplicableManual()
    {
        var evidence = ArtifactEvidenceCollector.Collect(_tempDir, "fake-type");

        evidence.Fitness.ShouldBe(AppTypeFitness.NotApplicable);
        evidence.SuggestedStrategy.ShouldBe(SuggestedStrategies.Manual);
    }
}
