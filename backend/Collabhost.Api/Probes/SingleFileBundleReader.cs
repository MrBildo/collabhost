using System.Text;

namespace Collabhost.Api.Probes;

// Reads the .NET single-file publish bundle header from the tail of a binary.
// Bundle layout (documented in dotnet/runtime, Microsoft.NET.HostModel.Bundle.Manifest):
//
//   <appended payload>
//   <bundle header>          variable-length, version-dependent
//   <Int64 header offset>    8 bytes: byte position of bundle header in file
//   <16-byte signature>      stable across all bundle major versions
//
// The 16-byte signature is the authoritative single-file marker. The bundle ID
// (string immediately after the major/minor version Int32s in the header) carries
// the GUID-style short identifier, and -- since bundle major version 2 -- the
// embedded ".runtimeconfig.json" file's location is known via the table of
// contents that follows. We only need a best-effort runtime-version + ASP.NET
// Core marker here; full table-of-contents traversal is deferred (card #220 §5
// scope cut on .deps.json extraction).
//
// Card #220: degraded fallback per Bill ruling 3 -- if the format changes in a
// future .NET release, the reader returns null for the metadata fields and the
// probe panel renders without runtime info.
public static class SingleFileBundleReader
{
    // Authoritative bundle marker -- placed at end-of-file, just after the Int64
    // header offset. Source: dotnet/runtime Microsoft.NET.HostModel.Bundle.Manifest.
    private static readonly byte[] _bundleSignature =
    [
        0x8b, 0xca, 0xcf, 0xc9, 0xa0, 0x46, 0x8b, 0xc0,
        0x73, 0x4d, 0xe9, 0xc2, 0xc2, 0xc4, 0xa1, 0x4d
    ];

    // Bound the tail-read so a non-bundle binary doesn't pull megabytes into memory.
    private const int _tailReadLimit = 64 * 1024;

    // Entry types in the bundle table of contents (post bundle major version 2).
    // We only care about RuntimeConfigJson here.
    private const byte _entryTypeRuntimeConfigJson = 4;

    public static SingleFileBundleProbe? TryRead(string filePath)
    {
        try
        {
            // Defense in depth against opening Linux FIFOs / sockets / device
            // files: stat first, reject zero-length entries before any open()
            // call. File.OpenRead on a FIFO blocks indefinitely waiting for a
            // writer; FileInfo.Length stat does not. A real .NET single-file
            // bundle is megabytes -- the floor here is safe. Card #220 follow-up.
            var info = new FileInfo(filePath);

            if (info.Length < _bundleSignature.Length + sizeof(long))
            {
                return null;
            }

            using var stream = File.OpenRead(filePath);

            return TryReadCore(stream);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Internal seam exposed for deterministic testing against a Stream.
    internal static SingleFileBundleProbe? TryReadCore(Stream stream)
    {
        if (!stream.CanSeek || !stream.CanRead)
        {
            return null;
        }

        var length = stream.Length;

        if (length < _bundleSignature.Length + sizeof(long))
        {
            return null;
        }

        var tailLength = (int)Math.Min(length, _tailReadLimit);

        stream.Seek(length - tailLength, SeekOrigin.Begin);

        var tail = new byte[tailLength];
        var read = ReadExact(stream, tail);

        if (read != tailLength)
        {
            return null;
        }

        var signatureIndex = LastIndexOf(tail, _bundleSignature);

        if (signatureIndex < sizeof(long))
        {
            return null;
        }

        // Header offset Int64 lives immediately before the signature.
        var headerOffset = BitConverter.ToInt64(tail, signatureIndex - sizeof(long));

        if (headerOffset <= 0 || headerOffset >= length)
        {
            return null;
        }

        // Read the bundle header, bounded to a small max read.
        const int maxHeaderBytes = 16 * 1024;
        var headerLength = (int)Math.Min(length - headerOffset, maxHeaderBytes);

        if (headerLength <= 0)
        {
            return null;
        }

        stream.Seek(headerOffset, SeekOrigin.Begin);

        var headerBuffer = new byte[headerLength];

        return ReadExact(stream, headerBuffer) != headerLength
            ? null
            : ParseHeader(headerBuffer, stream);
    }

    private static SingleFileBundleProbe? ParseHeader(byte[] header, Stream sourceStream)
    {
        try
        {
            using var memory = new MemoryStream(header, writable: false);
            using var reader = new BinaryReader(memory);

            var majorVersion = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // minor version

            // We only know how to read major version 1+. Anything beyond what we've
            // seen, we degrade to "single-file detected, no metadata."
            if (majorVersion is < 1 or > 32)
            {
                return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: false);
            }

            var fileCount = reader.ReadInt32();

            if (fileCount is < 0 or > 10_000)
            {
                return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: false);
            }

            // Bundle ID is a length-prefixed string (BinaryReader.ReadString uses 7-bit
            // encoded length prefix -- matches BinaryWriter on the producing side).
            _ = reader.ReadString(); // bundle ID

            // Major version 2+ has DepsJson + RuntimeConfigJson offsets/sizes immediately
            // after the bundle ID. We don't use them directly -- we walk the table of
            // contents which is more stable.
            if (majorVersion >= 2)
            {
                _ = reader.ReadInt64(); // depsJsonOffset
                _ = reader.ReadInt64(); // depsJsonSize
                _ = reader.ReadInt64(); // runtimeConfigJsonOffset
                _ = reader.ReadInt64(); // runtimeConfigJsonSize
                _ = reader.ReadUInt64(); // bundle flags
            }

            // Table of contents: file count entries, each
            //   Int64 offset, Int64 size, [Int64 compressedSize if v6+], byte type, string relativePath
            for (var i = 0; i < fileCount; i++)
            {
                var offset = reader.ReadInt64();
                var size = reader.ReadInt64();

                if (majorVersion >= 6)
                {
                    _ = reader.ReadInt64(); // compressed size
                }

                var entryType = reader.ReadByte();
                _ = reader.ReadString(); // relative path

                if (entryType == _entryTypeRuntimeConfigJson)
                {
                    return ReadRuntimeConfigJson(sourceStream, offset, size);
                }
            }

            // Format parsed successfully but no runtimeconfig entry found -- still a
            // single-file bundle, just nothing useful to extract.
            return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: true);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException or ArgumentException)
        {
            return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: false);
        }
    }

    private static SingleFileBundleProbe? ReadRuntimeConfigJson
    (
        Stream sourceStream,
        long offset,
        long size
    )
    {
        // TOC offsets index the ORIGINAL file, not the header buffer. Seek the
        // source stream that TryReadCore handed us.
        if (size is <= 0 or > 1_000_000)
        {
            return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: true);
        }

        try
        {
            sourceStream.Seek(offset, SeekOrigin.Begin);

            var jsonBytes = new byte[size];

            if (ReadExact(sourceStream, jsonBytes) != size)
            {
                return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: true);
            }

            var json = Encoding.UTF8.GetString(jsonBytes);

            return ParseRuntimeConfig(json);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: true);
        }
    }

    private static SingleFileBundleProbe ParseRuntimeConfig(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("runtimeOptions", out var runtimeOptions))
            {
                return new SingleFileBundleProbe(null, null, null, false, FormatRecognized: true);
            }

            var tfm = runtimeOptions.TryGetProperty("tfm", out var tfmElement)
                ? tfmElement.GetString()
                : null;

            string? runtimeVersion = null;
            var isAspNetCore = false;

            // Check both "frameworks" (framework-dependent) and "includedFrameworks"
            // (self-contained); for self-contained single-file publishes the entries
            // typically live in "includedFrameworks" but neither shape is guaranteed.
            if (TryReadFrameworks(runtimeOptions, "includedFrameworks", out var includedRuntime, out var includedAspNetCore))
            {
                runtimeVersion ??= includedRuntime;
                isAspNetCore = isAspNetCore || includedAspNetCore;
            }

            if (TryReadFrameworks(runtimeOptions, "frameworks", out var frameworksRuntime, out var frameworksAspNetCore))
            {
                runtimeVersion ??= frameworksRuntime;
                isAspNetCore = isAspNetCore || frameworksAspNetCore;
            }

            return new SingleFileBundleProbe(tfm, runtimeVersion, isAspNetCore, true, FormatRecognized: true);
        }
        catch (JsonException)
        {
            return new SingleFileBundleProbe(null, null, null, true, FormatRecognized: true);
        }
    }

    private static bool TryReadFrameworks
    (
        JsonElement parent,
        string propertyName,
        out string? runtimeVersion,
        out bool isAspNetCore
    )
    {
        runtimeVersion = null;
        isAspNetCore = false;

        if (!parent.TryGetProperty(propertyName, out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var any = false;

        foreach (var item in array.EnumerateArray())
        {
            any = true;

            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var version = item.TryGetProperty("version", out var v) ? v.GetString() : null;

            if (string.Equals(name, "Microsoft.AspNetCore.App", StringComparison.Ordinal))
            {
                isAspNetCore = true;
            }

            if (runtimeVersion is null && version is not null)
            {
                runtimeVersion = version;
            }
        }

        return any;
    }

    private static int LastIndexOf(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return -1;
        }

        for (var i = haystack.Length - needle.Length; i >= 0; i--)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ReadExact(Stream stream, byte[] buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var n = stream.Read(buffer, read, buffer.Length - read);

            if (n == 0)
            {
                break;
            }

            read += n;
        }

        return read;
    }
}

// FormatRecognized=false signals the bundle layout fell outside what the reader
// can parse (future .NET version with a bumped major version, malformed header).
// IsSelfContained is best-effort -- "true" only when we could read framework
// entries from "includedFrameworks". Card #220 Bill ruling 3 (degraded fallback).
public record SingleFileBundleProbe
(
    string? Tfm,
    string? RuntimeVersion,
    bool? IsAspNetCore,
    bool IsSelfContained,
    bool FormatRecognized
);
