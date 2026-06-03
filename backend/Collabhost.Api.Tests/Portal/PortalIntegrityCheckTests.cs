using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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

    [SkippableFact]
    public void ComputeWwwrootHash_DualCompute_CSharpEqualsBashScript()
    {
        // The seam check the known-vector test cannot make: run the REAL C#
        // ComputeWwwrootHash and the REAL bash script (tools/compute-wwwroot-hash.sh
        // -- the single bash implementation publish.yml + publish-dryrun.yml also
        // call) over the SAME non-trivial fixture tree and assert they agree byte
        // for byte. The known-vector test pins C# to its own hand-spec; this pins
        // C# against the bash side so neither can drift silently (Card #395 / the
        // #342 hash-contract chain). The fixture deliberately exercises the
        // dimensions the 2-file known vector does not: deep nesting (path-
        // separator normalization), ordinal sort across multiple path depths,
        // and varied content sizes.
        var scriptPath = LocateRepoToolScript("compute-wwwroot-hash.sh");
        Skip.If
        (
            scriptPath is null,
            "tools/compute-wwwroot-hash.sh not found by walking up from the test "
            + "base directory -- cannot run the dual-compute seam check."
        );

        var wwwrootPath = Path.Combine(_baseDirectory, "wwwroot");
        Directory.CreateDirectory(Path.Combine(wwwrootPath, "assets", "nested", "deeper"));
        Directory.CreateDirectory(Path.Combine(wwwrootPath, "assets", "vendor"));

        // Names chosen so the ordinal sort order is non-obvious: "assets/Z.js"
        // sorts before "assets/a.js" under ordinal (uppercase < lowercase), and
        // the nested paths interleave with the top-level ones.
        File.WriteAllText(Path.Combine(wwwrootPath, "index.html"), "<!doctype html>\nshell body\n");
        File.WriteAllText(Path.Combine(wwwrootPath, "robots.txt"), "User-agent: *\n");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "Z.js"), "uppercase-Z-content");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "a.js"), "lowercase-a, longer content payload here");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "vendor", "lib.js"), "vendor lib");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "nested", "mid.css"), "/* mid */");
        File.WriteAllText(Path.Combine(wwwrootPath, "assets", "nested", "deeper", "leaf.json"), "{\"k\":\"v\"}");

        var csharpHash = PortalIntegrityCheck.ComputeWwwrootHash(wwwrootPath);
        var bashHash = RunBashHashScript(scriptPath!, wwwrootPath);

        bashHash.ShouldBe
        (
            csharpHash,
            "The bash script (tools/compute-wwwroot-hash.sh) and C# "
            + "PortalIntegrityCheck.ComputeWwwrootHash produced different digests over "
            + "the same wwwroot tree. The two implementations have drifted -- this is "
            + "the #342 contract gap #395 closed. PortalIntegrityCheck would report "
            + "false Drift/Ok on every operator install if this ships."
        );
    }

    // Walk up from the test assembly's base directory to the repo root (the
    // directory containing tools/) and return the requested script's full path,
    // or null if not found. Tests run with their output dir as the base; the repo
    // root is several levels up (backend/Collabhost.Api.Tests/bin/<cfg>/<tfm>/).
    private static string? LocateRepoToolScript(string scriptName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", scriptName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    // Shell the bash hash script over the fixture tree. Soft-skips (SkipException)
    // when no usable bash is found -- a developer box without Git Bash / WSL still
    // exercises every other test, and CI (ubuntu native bash + windows-latest Git
    // Bash) runs the seam for real every push.
    private static string RunBashHashScript(string scriptPath, string wwwrootPath)
    {
        var bashPath = ResolveBash();
        Skip.If
        (
            bashPath is null,
            "No usable bash found (Git Bash on Windows / bash on PATH) -- skipping the "
            + "C#<->bash dual-compute seam check. CI runs it on ubuntu (native bash) and "
            + "windows-latest (Git Bash)."
        );

        var startInfo = new ProcessStartInfo
        {
            FileName = bashPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Forward-slash the paths: Git Bash / msys mangles backslashes in arguments
        // (C:\a\b -> C:ab). Drive-letter forward-slash paths (C:/a/b) are accepted
        // by Git Bash and are native on Linux/macOS, so this is correct everywhere.
        // We deliberately resolve Git Bash explicitly on Windows rather than letting
        // Process.Start pick "bash" off PATH -- the WSL launcher (System32\bash.exe)
        // often shadows Git Bash and has a different filesystem view (/mnt/c, not
        // C:/), which would make the shell-out fail in a host-dependent way.
        startInfo.ArgumentList.Add(scriptPath.Replace('\\', '/'));
        startInfo.ArgumentList.Add(wwwrootPath.Replace('\\', '/'));

        Process? process;

        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            throw new SkipException
            (
                "bash could not be started -- skipping the C#<->bash dual-compute seam "
                + "check. CI runs it on ubuntu (native bash) and windows-latest (Git Bash)."
            );
        }

        if (process is null)
        {
            throw new SkipException("Failed to start bash for the dual-compute seam check.");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        var exitCode = process.ExitCode;

        exitCode.ShouldBe
        (
            0,
            string.Create
            (
                CultureInfo.InvariantCulture,
                $"tools/compute-wwwroot-hash.sh exited {exitCode}. stderr: {stderr}"
            )
        );

        return stdout.Trim();
    }

    // Resolve a bash that can open a C:/-style script path and run the hash
    // algorithm. On Linux/macOS that is plain "bash" off PATH. On Windows it is
    // Git Bash at one of its standard install locations -- NOT the WSL launcher
    // (System32\bash.exe), whose /mnt/c filesystem view breaks the C:/ paths we
    // pass. Returns null when none is found (the caller soft-skips).
    private static string? ResolveBash()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        string[] candidates =
        [
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files\Git\usr\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe"
        ];

        return Array.Find(candidates, File.Exists);
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
