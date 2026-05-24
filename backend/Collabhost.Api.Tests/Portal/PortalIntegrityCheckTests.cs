using System.Text;

using Collabhost.Api.Portal;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Portal;

public class PortalIntegrityCheckTests : IDisposable
{
    private readonly string _baseDirectory;

    public PortalIntegrityCheckTests()
    {
        _baseDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-integrity-tests", Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Validate_EmptyExpectedHash_ReportsUnknown()
    {
        SeedWwwroot();

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, expectedHash: "", NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Unknown);
        outcome.ExpectedHash.ShouldBe(string.Empty);
        outcome.ActualHash.ShouldBe(string.Empty);
        outcome.RecoverySteps.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WhitespaceExpectedHash_ReportsUnknown()
    {
        SeedWwwroot();

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, expectedHash: "   ", NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Unknown);
    }

    [Fact]
    public void Validate_NoWwwrootDirectory_ReportsUnknown()
    {
        // PortalReachabilityCheck already warns on this state; integrity check should not
        // double-warn or attempt to hash a non-existent tree.
        var outcome = PortalIntegrityCheck.Validate
        (
            _baseDirectory,
            expectedHash: "deadbeef",
            NullLogger.Instance
        );

        outcome.Status.ShouldBe(PortalIntegrityStatus.Unknown);
        outcome.ExpectedHash.ShouldBe("deadbeef");
        outcome.ActualHash.ShouldBe(string.Empty);
    }

    [Fact]
    public void Validate_HashMatches_ReportsOk()
    {
        SeedWwwroot();

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        var actualHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, actualHash, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Ok);
        outcome.ExpectedHash.ShouldBe(actualHash);
        outcome.ActualHash.ShouldBe(actualHash);
        outcome.RecoverySteps.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_HashMatchCaseInsensitive_ReportsOk()
    {
        SeedWwwroot();

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        var actualHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        // ComputeWwwrootHash returns lowercase. Validate via uppercase to confirm
        // the comparison is case-insensitive (defensive against operator-side casing).
        var outcome = PortalIntegrityCheck.Validate
        (
            _baseDirectory,
            actualHash.ToUpperInvariant(),
            NullLogger.Instance
        );

        outcome.Status.ShouldBe(PortalIntegrityStatus.Ok);
    }

    [Fact]
    public void Validate_ContentDriftAfterSeeding_ReportsDrift()
    {
        SeedWwwroot();

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        var seededHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        // Mutate a file's content (same size, different bytes wouldn't matter -- any change
        // in size or content changes the hash because per-file content sha is part of the input).
        File.WriteAllText
        (
            Path.Combine(wwwrootPath, "index.html"),
            "<!doctype html><html><body>stripped</body></html>"
        );

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, seededHash, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Drift);
        outcome.ExpectedHash.ShouldBe(seededHash);
        outcome.ActualHash.ShouldNotBe(seededHash);
        outcome.RecoverySteps.ShouldNotBeEmpty();
    }

    [Fact]
    public void Validate_FileRemoved_ReportsDrift()
    {
        SeedWwwroot();

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        var seededHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        // Partial-strip variant: delete a file. The hash MUST change because the file list
        // is part of the deterministic input.
        File.Delete(Path.Combine(wwwrootPath, "assets", "bundle-abc123.js"));

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, seededHash, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Drift);
        outcome.ActualHash.ShouldNotBe(seededHash);
    }

    [Fact]
    public void Validate_FileAdded_ReportsDrift()
    {
        SeedWwwroot();

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        var seededHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        // Adding an extra file (operator dropping a custom asset) is also drift -- the
        // file list grew so the hash must change.
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "extra.css"), "/* added */");

        var outcome = PortalIntegrityCheck.Validate(_baseDirectory, seededHash, NullLogger.Instance);

        outcome.Status.ShouldBe(PortalIntegrityStatus.Drift);
    }

    [Fact]
    public void ComputeWwwrootHash_SameContent_ReturnsSameHash()
    {
        var wwwrootA = Path.Combine(_baseDirectory, "a");
        var wwwrootB = Path.Combine(_baseDirectory, "b");

        SeedWwwrootAt(wwwrootA);
        SeedWwwrootAt(wwwrootB);

        var hashA = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootA);
        var hashB = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootB);

        hashA.ShouldBe(hashB);
        hashA.Length.ShouldBe(64);
        hashA.ShouldMatch("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeWwwrootHash_NestedDirectoryStructure_HashesAllFiles()
    {
        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        Directory.CreateDirectory(Path.Combine(wwwrootPath, "assets", "nested", "deeper"));

        File.WriteAllText(Path.Combine(wwwrootPath, "index.html"), "shell");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "bundle.js"), "top");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "nested", "mid.js"), "mid");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "nested", "deeper", "leaf.js"), "leaf");

        var hashWithAllFiles = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        // Removing the deepest file MUST change the hash. Confirms recursive enumeration.
        File.Delete(Path.Combine(wwwrootPath, "assets", "nested", "deeper", "leaf.js"));

        var hashWithoutLeaf = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        hashWithoutLeaf.ShouldNotBe(hashWithAllFiles);
    }

    [Fact]
    public void ComputeWwwrootHash_KnownInput_MatchesExpectedSpec()
    {
        // Lock the hash algorithm contract to a known vector. The publish workflow's bash
        // step (.github/workflows/publish.yml "Compute wwwroot hash") MUST produce this same
        // digest for the same input -- if either side drifts, this test or the workflow
        // verify step catches it.
        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        Directory.CreateDirectory(Path.Combine(wwwrootPath, "assets"));

        File.WriteAllText(Path.Combine(wwwrootPath, "index.html"), "hello");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "a.js"), "world");

        // Expected: SHA-256 over the concatenation of
        //   "assets/a.js\n5\n<sha256(world)>\n" + "index.html\n5\n<sha256(hello)>\n"
        // (ordinal-sorted relative POSIX paths, per-file size + per-file sha256 hex).
        var expected = ComputeExpectedVector("assets/a.js", "world", "index.html", "hello");

        var actual = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);

        actual.ShouldBe(expected);
    }

    private void SeedWwwroot() => SeedWwwrootAt(Path.Combine(_baseDirectory, "wwwroot"));

    private static void SeedWwwrootAt(string wwwrootPath)
    {
        Directory.CreateDirectory(Path.Combine(wwwrootPath, "assets"));
        File.WriteAllText
        (
            Path.Combine(wwwrootPath, "index.html"),
            "<!doctype html><html><body data-test=\"seeded\"></body></html>"
        );
        File.WriteAllText
        (
            Path.Combine(wwwrootPath, "assets", "bundle-abc123.js"),
            "// seeded bundle\n"
        );
        File.WriteAllText
        (
            Path.Combine(wwwrootPath, "assets", "style-def456.css"),
            "/* seeded styles */\n"
        );
    }

    private static string ComputeExpectedVector(params string[] pathContentPairs)
    {
        // pathContentPairs is (relPath, content, relPath, content, ...) -- already in the
        // ordinal-sort order the algorithm expects.
        using var aggregate = System.Security.Cryptography.IncrementalHash.CreateHash
        (
            System.Security.Cryptography.HashAlgorithmName.SHA256
        );

        for (var i = 0; i < pathContentPairs.Length; i += 2)
        {
            var relPath = pathContentPairs[i];
            var content = pathContentPairs[i + 1];
            var contentBytes = Encoding.UTF8.GetBytes(content);
            var contentHash = Convert.ToHexStringLower
            (
                System.Security.Cryptography.SHA256.HashData(contentBytes)
            );

            var line = $"{relPath}\n{contentBytes.Length}\n{contentHash}\n";
            aggregate.AppendData(Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }
}
