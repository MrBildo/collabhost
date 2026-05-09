using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

public class ExecutableExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public ExecutableExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-exec-tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Extract_NoEvidenceSignal_ReturnsNull()
    {
        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.NotApplicable,
            SuggestedStrategies.Manual,
            [],
            null
        );

        var result = ExecutableExtractor.Extract(_tempDir, evidence);

        result.ShouldBeNull();
    }

    [Fact]
    public void Extract_BinaryAtRootSignal_ReturnsRawDataWithBinaryName()
    {
        var binaryPath = Path.Combine(_tempDir, "myapp.exe");

        File.WriteAllText(binaryPath, "fake-binary-payload");

        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.FullMatch,
            SuggestedStrategies.Manual,
            [
                new EvidenceSignal
                (
                    EvidenceSignalKinds.BinaryAtRoot,
                    "myapp.exe",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["count"] = "1",
                        ["binaryName"] = "myapp.exe",
                        ["isManagedDotnet"] = "false"
                    }
                )
            ],
            RuntimeFamilies.Executable
        );

        var result = ExecutableExtractor.Extract(_tempDir, evidence);

        result.ShouldNotBeNull();
        result.BinaryName.ShouldBe("myapp.exe");
        result.CandidateBinaryCount.ShouldBe(1);
        result.IsManagedDotnet.ShouldBeFalse();
        result.BinarySizeBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Extract_MultipleCandidates_ReturnsCount()
    {
        File.WriteAllText(Path.Combine(_tempDir, "myapp.exe"), "x");

        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.LikelyMatch,
            SuggestedStrategies.Manual,
            [
                new EvidenceSignal
                (
                    EvidenceSignalKinds.BinaryAtRoot,
                    "myapp.exe",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["count"] = "3",
                        ["binaryName"] = "myapp.exe",
                        ["isManagedDotnet"] = "true"
                    }
                )
            ],
            RuntimeFamilies.Executable
        );

        var result = ExecutableExtractor.Extract(_tempDir, evidence);

        result.ShouldNotBeNull();
        result.CandidateBinaryCount.ShouldBe(3);
        result.IsManagedDotnet.ShouldBeTrue();
    }

    [Fact]
    public void Extract_BinaryFileMissing_ReturnsZeroSize()
    {
        // Evidence claims a binary but the file is gone.
        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.FullMatch,
            SuggestedStrategies.Manual,
            [
                new EvidenceSignal
                (
                    EvidenceSignalKinds.BinaryAtRoot,
                    "ghost.exe",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["count"] = "1",
                        ["binaryName"] = "ghost.exe",
                        ["isManagedDotnet"] = "false"
                    }
                )
            ],
            RuntimeFamilies.Executable
        );

        var result = ExecutableExtractor.Extract(_tempDir, evidence);

        result.ShouldNotBeNull();
        result.BinarySizeBytes.ShouldBe(0);
    }
}
