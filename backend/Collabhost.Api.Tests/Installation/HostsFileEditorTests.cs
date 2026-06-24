using Collabhost.Api.Installation;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Installation;

public class HostsFileEditorTests : IDisposable
{
    private readonly string _scratchDir;

    public HostsFileEditorTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"collabhost-hosts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            try
            {
                Directory.Delete(_scratchDir, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Rewrite_NoExistingMarkers_AppendsBlockWithBlankLineSeparator()
    {
        var path = WriteHosts(
            """
            127.0.0.1   localhost
            ::1         localhost
            """);

        var body = "127.0.0.1\tcollabhost.collab.internal\n127.0.0.1\tmyapp.collab.internal";

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Appended);
        result.Warning.ShouldBeNull();

        var content = File.ReadAllText(path);
        content.ShouldContain("127.0.0.1   localhost");
        content.ShouldContain(HostsFileEditor.BeginMarker);
        content.ShouldContain(HostsFileEditor.EndMarker);
        content.ShouldContain("myapp.collab.internal");
    }

    [Fact]
    public void Rewrite_NoChange_DoesNotTouchFile()
    {
        var body = "127.0.0.1\tcollabhost.collab.internal\n127.0.0.1\tmyapp.collab.internal";

        var initial = string.Join
        (
            "\n",
            "127.0.0.1   localhost",
            string.Empty,
            HostsFileEditor.BeginMarker,
            body,
            HostsFileEditor.EndMarker,
            string.Empty
        );

        var path = WriteHosts(initial);

        // Pin a sentinel mtime well in the past so any rewrite (which goes through atomic Move
        // + replace) would bump it -- the no-change branch must NOT bump it.
        var sentinel = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(path, sentinel);

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.NoChange);

        var afterMtime = File.GetLastWriteTimeUtc(path);
        afterMtime.ShouldBe(sentinel);
    }

    [Fact]
    public void Rewrite_ExistingBlockDifferentContent_ReplacesBlockPreservesSurround()
    {
        var initialBody = "127.0.0.1\told-app.collab.internal";
        var lf = "\n";

        var initial = string.Join
        (
            lf,
            "# Hosts file header comment",
            "127.0.0.1   localhost",
            string.Empty,
            HostsFileEditor.BeginMarker,
            initialBody,
            HostsFileEditor.EndMarker,
            string.Empty,
            "# Footer comment kept intact",
            "192.168.1.1   router.local"
        );

        var path = WriteHosts(initial);

        var newBody = "127.0.0.1\tcollabhost.collab.internal\n127.0.0.1\tnew-app.collab.internal";

        var result = HostsFileEditor.Rewrite(path, newBody);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Replaced);

        var content = File.ReadAllText(path);
        content.ShouldContain("# Hosts file header comment");
        content.ShouldContain("# Footer comment kept intact");
        content.ShouldContain("192.168.1.1   router.local");
        content.ShouldContain("new-app.collab.internal");
        content.ShouldNotContain("old-app.collab.internal");
    }

    [Fact]
    public void Rewrite_OrphanBegin_AppendsFreshBlockWithWarning()
    {
        // Operator hand-edit corruption: BEGIN line present, END marker absent.
        var initial = string.Join
        (
            "\n",
            "127.0.0.1   localhost",
            HostsFileEditor.BeginMarker,
            "127.0.0.1   orphan.local"
        );

        var path = WriteHosts(initial);

        var body = "127.0.0.1\tcollabhost.collab.internal";

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.AppendedWithOrphan);
        result.Warning.ShouldNotBeNull();
        result.Warning.ShouldContain("orphan");

        var content = File.ReadAllText(path);
        content.ShouldContain("collabhost.collab.internal");
        content.ShouldContain(HostsFileEditor.EndMarker);
    }

    [Fact]
    public void Rewrite_EmptyExistingFile_CreatesBlockOnly()
    {
        var path = Path.Combine(_scratchDir, "hosts");
        File.WriteAllText(path, string.Empty);

        var body = "127.0.0.1\tcollabhost.collab.internal";

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Appended);

        var content = File.ReadAllText(path);
        content.ShouldStartWith(HostsFileEditor.BeginMarker);
        content.ShouldContain("collabhost.collab.internal");
        content.ShouldContain(HostsFileEditor.EndMarker);
    }

    [Fact]
    public void Rewrite_FileDoesNotExist_TreatedAsEmptyAndAppends()
    {
        var path = Path.Combine(_scratchDir, "missing-hosts");

        var body = "127.0.0.1\tcollabhost.collab.internal";

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Appended);
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void Rewrite_PreservesCrlfWhenFileUsesCrlf()
    {
        var path = Path.Combine(_scratchDir, "hosts");
        File.WriteAllText(path, "127.0.0.1\tlocalhost\r\n");

        var body = "127.0.0.1\tcollabhost.collab.internal";

        HostsFileEditor.Rewrite(path, body);

        var content = File.ReadAllText(path);
        content.ShouldContain("\r\n");
    }

    [Fact]
    public void Rewrite_OperatorEditedBetweenMarkersIsOverwritten()
    {
        var body = "127.0.0.1\tcollabhost.collab.internal";

        var initial = string.Join
        (
            "\n",
            HostsFileEditor.BeginMarker,
            "127.0.0.1   operator-added-this.local",
            HostsFileEditor.EndMarker
        );

        var path = WriteHosts(initial);

        var result = HostsFileEditor.Rewrite(path, body);

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Replaced);

        var content = File.ReadAllText(path);
        content.ShouldNotContain("operator-added-this.local");
        content.ShouldContain("collabhost.collab.internal");
    }

    [Fact]
    public void ComposeBlockBody_EmptyList_ReturnsEmptyString()
    {
        var body = HostsFileEditor.ComposeBlockBody([], "\n");
        body.ShouldBeEmpty();
    }

    [Fact]
    public void ComposeBlockBody_OneEntry_NoTrailingNewline()
    {
        var body = HostsFileEditor.ComposeBlockBody
        (
            [new HostsFileEntry("127.0.0.1", "collabhost.collab.internal")],
            "\n"
        );

        body.ShouldBe("127.0.0.1\tcollabhost.collab.internal");
    }

    [Fact]
    public void ComposeBlockBody_MultipleEntries_JoinedByLineEnding()
    {
        var body = HostsFileEditor.ComposeBlockBody
        (
            [
                new HostsFileEntry("127.0.0.1", "a.local"),
                new HostsFileEntry("127.0.0.1", "b.local")
            ],
            "\n"
        );

        body.ShouldBe("127.0.0.1\ta.local\n127.0.0.1\tb.local");
    }

    [Fact]
    public void DetectLineEnding_FileWithCrlf_ReturnsCrlf() =>
        HostsFileEditor.DetectLineEnding("line1\r\nline2").ShouldBe("\r\n");

    [Fact]
    public void DetectLineEnding_FileWithLf_ReturnsLf() =>
        HostsFileEditor.DetectLineEnding("line1\nline2").ShouldBe("\n");

    [Fact]
    public void DetectLineEnding_EmptyFile_ReturnsPlatformNative()
    {
        var expected = OperatingSystem.IsWindows() ? "\r\n" : "\n";
        HostsFileEditor.DetectLineEnding(string.Empty).ShouldBe(expected);
    }

    [Fact]
    public void Rewrite_RepeatedRunsWithEndMarkerStripped_ConvergesToSingleBlock()
    {
        // INS-01: an external editor that strips the END marker between every run leaves the prior
        // run's orphan BEGIN in place. Pre-fix, each run could not find a complete block to replace
        // and blindly APPENDED a fresh one, so the managed blocks accumulated (BEGIN-count 1, 2, 3,
        // 4, ...). Post-fix, the append path reconciles -- it strips ALL marker artifacts before
        // appending -- so repeated runs converge to exactly one block regardless of the external
        // corruption.
        var path = WriteHosts("127.0.0.1\tlocalhost\n");
        var body = "127.0.0.1\tcollabhost.collab.internal";

        for (var run = 0; run < 4; run++)
        {
            HostsFileEditor.Rewrite(path, body);

            // Simulate the external marker-stripping editor: drop the END line before the next run.
            var corrupted = File.ReadAllText(path)
                .Replace("\n" + HostsFileEditor.EndMarker, string.Empty, StringComparison.Ordinal);
            File.WriteAllText(path, corrupted);
        }

        // After the final run, restore by rewriting once more without the external stripper, then
        // assert exactly one BEGIN marker survived across the whole sequence.
        HostsFileEditor.Rewrite(path, body);

        var content = File.ReadAllText(path);

        CountOccurrences(content, HostsFileEditor.BeginMarkerPrefix).ShouldBe(1);
        CountOccurrences(content, HostsFileEditor.EndMarker).ShouldBe(1);
        content.ShouldContain("collabhost.collab.internal");
        // The operator's own entry is never touched by the reconcile.
        content.ShouldContain("127.0.0.1\tlocalhost");
    }

    [Fact]
    public void Rewrite_DuplicateBeginTangle_CollapsesToSingleBlock()
    {
        // A duplicate-BEGIN tangle (two BEGIN lines, one END). ExtractBlockBody anchors on the FIRST
        // BEGIN and the FIRST END after it, so the matched span SWALLOWS the inner duplicate BEGIN
        // and both stale bodies into "the block" -- the Replace path then overwrites the entire span
        // with one canonical block. The load-bearing property is the outcome: exactly one block, no
        // stale entries left behind (regardless of whether the path taken was Replace or the
        // orphan-reconcile -- here it is Replace).
        var initial = string.Join
        (
            "\n",
            "127.0.0.1\tlocalhost",
            HostsFileEditor.BeginMarker,
            "127.0.0.1\tstale-one.local",
            HostsFileEditor.BeginMarker,
            "127.0.0.1\tstale-two.local",
            HostsFileEditor.EndMarker
        );

        var path = WriteHosts(initial);

        var result = HostsFileEditor.Rewrite(path, "127.0.0.1\tcollabhost.collab.internal");

        result.Outcome.ShouldBe(HostsFileEditor.RewriteOutcome.Replaced);

        var content = File.ReadAllText(path);

        CountOccurrences(content, HostsFileEditor.BeginMarkerPrefix).ShouldBe(1);
        CountOccurrences(content, HostsFileEditor.EndMarker).ShouldBe(1);
        content.ShouldContain("collabhost.collab.internal");
        content.ShouldNotContain("stale-one.local");
        content.ShouldNotContain("stale-two.local");
        content.ShouldContain("127.0.0.1\tlocalhost");
    }

    private static int CountOccurrences(string content, string token)
    {
        var count = 0;
        var index = 0;

        while ((index = content.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }

    private string WriteHosts(string content)
    {
        var path = Path.Combine(_scratchDir, "hosts");
        File.WriteAllText(path, content);
        return path;
    }
}
