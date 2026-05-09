using System.Text;

using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Card #220 -- DotnetExtractor's single-file branch synthesizes a minimal
// RawDotnetData when the evidence collector signals a self-contained .NET
// publish. Bundle bytes are constructed in memory to keep the test deterministic
// and avoid a multi-MB binary fixture.
public class DotnetExtractorSingleFileTests : IDisposable
{
    private static readonly byte[] _bundleSignature =
    [
        0x8b, 0xca, 0xcf, 0xc9, 0xa0, 0x46, 0x8b, 0xc0,
        0x73, 0x4d, 0xe9, 0xc2, 0xc2, 0xc4, 0xa1, 0x4d
    ];

    private const byte _entryTypeRuntimeConfigJson = 4;

    private readonly string _tempDir;

    public DotnetExtractorSingleFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "collabhost-dotnet-sf", Guid.NewGuid().ToString("N"));
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
    public void Extract_SingleFileBinaryWithRuntimeConfig_ReturnsMinimalDotnetData()
    {
        var binaryName = "MyApp.exe";
        var binaryPath = Path.Combine(_tempDir, binaryName);

        var bundleBytes = BuildBundle
        (
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
            """,
            majorVersion: 6
        );

        File.WriteAllBytes(binaryPath, bundleBytes);

        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.FullMatch,
            SuggestedStrategies.Manual,
            [new EvidenceSignal(EvidenceSignalKinds.SingleFileBinary, binaryName, null)],
            RuntimeFamilies.Dotnet
        );

        var result = DotnetExtractor.Extract(_tempDir, evidence);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBe("net10.0");

        result.RuntimeConfig.IncludedFrameworks.ShouldContain
        (
            f => f.Name == "Microsoft.NETCore.App" && f.Version == "10.0.5"
        );

        result.RuntimeConfig.IncludedFrameworks.ShouldContain
        (
            f => f.Name == "Microsoft.AspNetCore.App" && f.Version == "10.0.5"
        );
    }

    [Fact]
    public void Extract_SingleFileBinaryWithUnreadableBundle_DegradesToNullTfm()
    {
        // Bill ruling 3: degraded fallback. If the bundle reader can't parse the
        // header (bumped major version, malformed bytes), still emit a record so
        // the curator surfaces a panel with TFM=null.
        var binaryName = "MyApp.exe";
        var binaryPath = Path.Combine(_tempDir, binaryName);

        File.WriteAllBytes(binaryPath, [0x00, 0x01, 0x02, 0x03]);

        var evidence = new ArtifactEvidence
        (
            AppTypeFitness.FullMatch,
            SuggestedStrategies.Manual,
            [new EvidenceSignal(EvidenceSignalKinds.SingleFileBinary, binaryName, null)],
            RuntimeFamilies.Dotnet
        );

        var result = DotnetExtractor.Extract(_tempDir, evidence);

        result.ShouldNotBeNull();
        result.RuntimeConfig.ShouldNotBeNull();
        result.RuntimeConfig.Tfm.ShouldBeNull();
    }

    [Fact]
    public void Extract_NoEvidenceSignal_PreservesLegacyNullReturn()
    {
        // Empty directory, no evidence -- keep the pre-#220 behavior.
        var result = DotnetExtractor.Extract(_tempDir, evidence: null);

        result.ShouldBeNull();
    }

    private static byte[] BuildBundle(string runtimeConfigJson, uint majorVersion)
    {
        var runtimeConfigBytes = Encoding.UTF8.GetBytes(runtimeConfigJson);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Padding so the bundle header offset is > 0 (real binaries always
        // place the bundle past the original PE/ELF apphost contents).
        writer.Write(new byte[64]);

        var runtimeConfigOffset = ms.Position;

        writer.Write(runtimeConfigBytes);

        var headerOffset = ms.Position;

        writer.Write(majorVersion);
        writer.Write(0u);
        writer.Write(1); // file count
        writer.Write("BundleIdMock");

        if (majorVersion >= 2)
        {
            writer.Write(0L);
            writer.Write(0L);
            writer.Write(runtimeConfigOffset);
            writer.Write((long)runtimeConfigBytes.Length);
            writer.Write(0UL);
        }

        writer.Write(runtimeConfigOffset);
        writer.Write((long)runtimeConfigBytes.Length);

        if (majorVersion >= 6)
        {
            writer.Write(0L);
        }

        writer.Write(_entryTypeRuntimeConfigJson);
        writer.Write("runtimeconfig.json");

        writer.Write(headerOffset);
        writer.Write(_bundleSignature);

        return ms.ToArray();
    }
}
