using System.Text;

using Collabhost.Api.Probes;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Probes;

// Deterministic byte-sequence tests for the .NET single-file bundle reader.
// We construct synthetic bundles in memory rather than checking in a real binary
// fixture -- the construction matches what dotnet/runtime's BinaryWriter emits,
// and avoids carrying ~10MB of build-product into the test project.
public class SingleFileBundleReaderTests
{
    private static readonly byte[] _bundleSignature =
    [
        0x8b, 0xca, 0xcf, 0xc9, 0xa0, 0x46, 0x8b, 0xc0,
        0x73, 0x4d, 0xe9, 0xc2, 0xc2, 0xc4, 0xa1, 0x4d
    ];

    private const byte _entryTypeRuntimeConfigJson = 4;

    [Fact]
    public void TryRead_NotABundle_ReturnsNull()
    {
        // Just plain bytes -- no signature anywhere.
        using var stream = new MemoryStream(new byte[1024]);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldBeNull();
    }

    [Fact]
    public void TryRead_ValidBundleWithRuntimeConfig_ParsesTfmAndAspNetCore()
    {
        var runtimeConfig = """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "includedFrameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.5" },
                  { "name": "Microsoft.AspNetCore.App", "version": "10.0.5" }
                ]
              }
            }
            """;

        var bundle = BuildBundle(runtimeConfig, majorVersion: 6);

        using var stream = new MemoryStream(bundle);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldNotBeNull();
        probe.FormatRecognized.ShouldBeTrue();
        probe.IsSelfContained.ShouldBeTrue();
        probe.Tfm.ShouldBe("net10.0");
        probe.RuntimeVersion.ShouldBe("10.0.5");
        probe.IsAspNetCore.ShouldBe(true);
    }

    [Fact]
    public void TryRead_ValidBundleNonAspNetCore_ParsesAndReportsFalse()
    {
        var runtimeConfig = """
            {
              "runtimeOptions": {
                "tfm": "net10.0",
                "includedFrameworks": [
                  { "name": "Microsoft.NETCore.App", "version": "10.0.0" }
                ]
              }
            }
            """;

        var bundle = BuildBundle(runtimeConfig, majorVersion: 6);

        using var stream = new MemoryStream(bundle);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldNotBeNull();
        probe.IsAspNetCore.ShouldBe(false);
        probe.IsSelfContained.ShouldBeTrue();
    }

    [Fact]
    public void TryRead_BogusVersionInHeader_DegradesToNotRecognized()
    {
        // Major version bumped past what the reader claims to handle.
        var bundle = BuildBundle(runtimeConfigJson: "", majorVersion: 99, fileCountOverride: 0);

        using var stream = new MemoryStream(bundle);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldNotBeNull();
        probe.FormatRecognized.ShouldBeFalse();
        probe.Tfm.ShouldBeNull();
        probe.RuntimeVersion.ShouldBeNull();
    }

    [Fact]
    public void TryRead_MalformedRuntimeConfig_DegradesGracefully()
    {
        // Bundle is well-formed but the embedded JSON isn't.
        var bundle = BuildBundle("{ not json }", majorVersion: 6);

        using var stream = new MemoryStream(bundle);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldNotBeNull();
        probe.FormatRecognized.ShouldBeTrue();
        probe.Tfm.ShouldBeNull();
    }

    [Fact]
    public void TryRead_TooSmallToContainSignature_ReturnsNull()
    {
        // 4 bytes -- way too small to even hold the sig + offset.
        using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04]);

        var probe = SingleFileBundleReader.TryReadCore(stream);

        probe.ShouldBeNull();
    }

    // Builds a minimal-but-valid bundle layout:
    //   [filler header padding]
    //   [bundle header at offset H: majorVer, minorVer, fileCount, bundleId,
    //    (v2+: depsJson + runtimeConfigJson offsets/sizes + flags),
    //    (v6+: per-entry compressed sizes), TOC with one runtimeConfigJson entry]
    //   [signature offset H as Int64]
    //   [16-byte signature]
    private static byte[] BuildBundle(string runtimeConfigJson, uint majorVersion, int? fileCountOverride = null)
    {
        var runtimeConfigBytes = Encoding.UTF8.GetBytes(runtimeConfigJson);

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Pad the front so the header offset is > 0 (matches what a real
        // PE/ELF apphost looks like with the bundle appended past the binary).
        writer.Write(new byte[64]);

        // Embed the runtime config so we know its absolute offset before we
        // write the bundle header that references it.
        var runtimeConfigOffset = ms.Position;

        writer.Write(runtimeConfigBytes);

        // Bundle header begins here.
        var headerOffset = ms.Position;

        writer.Write(majorVersion);
        writer.Write(0u); // minor

        var fileCount = fileCountOverride ?? 1;

        writer.Write(fileCount);
        writer.Write("BundleIdMock");

        if (majorVersion >= 2)
        {
            writer.Write(0L); // depsJsonOffset
            writer.Write(0L); // depsJsonSize
            writer.Write(runtimeConfigOffset); // runtimeConfigJsonOffset
            writer.Write((long)runtimeConfigBytes.Length); // runtimeConfigJsonSize
            writer.Write(0UL); // flags
        }

        // TOC entries.
        for (var i = 0; i < fileCount; i++)
        {
            writer.Write(runtimeConfigOffset);
            writer.Write((long)runtimeConfigBytes.Length);

            if (majorVersion >= 6)
            {
                writer.Write(0L); // compressed size
            }

            writer.Write(_entryTypeRuntimeConfigJson);
            writer.Write("runtimeconfig.json"); // relative path
        }

        // Signature offset (Int64) immediately precedes the 16-byte signature.
        writer.Write(headerOffset);

        writer.Write(_bundleSignature);

        return ms.ToArray();
    }
}
