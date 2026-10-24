using Collabhost.Api.Authorization;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

public class FirstRunAdminKeyAnnouncerTests
{
    // Standard output counts as the journal only when JOURNAL_STREAM names the very socket fd 1 is.
    // The variable alone is not enough: it is inherited by processes whose output goes elsewhere (a
    // desktop terminal's shells, a CI step redirecting to a log), and those must still print the key.
    [Theory]
    [InlineData("8:12345", "socket:[12345]", true)]
    [InlineData("0:987654321", "socket:[987654321]", true)]
    [InlineData("8:12345", "socket:[99999]", false)]       // a different socket
    [InlineData("8:12345", "pipe:[12345]", false)]         // same number, not a socket
    [InlineData("8:12345", "/home/me/firstboot.log", false)] // redirected to a file
    [InlineData("8:12345", "/dev/pts/0", false)]           // a terminal
    [InlineData(null, "socket:[12345]", false)]            // not started by systemd
    [InlineData("", "socket:[12345]", false)]
    [InlineData("8:12345", null, false)]                   // fd 1 unreadable
    [InlineData("12345", "socket:[12345]", false)]         // malformed: no device part
    [InlineData("8:", "socket:[]", false)]                 // malformed: no inode
    [InlineData("8:12a45", "socket:[12a45]", false)]       // malformed: inode not a number
    public void IsJournalStream_MatchesOnlyTheJournalSocket(string? journalStream, string? standardOutputTarget, bool expected) =>
        FirstRunAdminKeyAnnouncer.IsJournalStream(journalStream, standardOutputTarget).ShouldBe(expected);

    [Fact]
    public void PrintToStandardOutput_WritesTheMarkerLine()
    {
        using var output = new StringWriter();

        var announcer = new FirstRunAdminKeyAnnouncer(Path.GetTempPath(), () => output, standardOutputIsJournal: false);

        announcer.PrintToStandardOutput("01JABCDEFGHJKMNPQRSTVWXYZ").ShouldBeTrue();

        output.ToString().ShouldBe($"Collabhost admin key: 01JABCDEFGHJKMNPQRSTVWXYZ{Environment.NewLine}");
    }

    [Fact]
    public void PrintToStandardOutput_WhenStandardOutputIsJournal_WritesNothing()
    {
        using var output = new StringWriter();

        var announcer = new FirstRunAdminKeyAnnouncer(Path.GetTempPath(), () => output, standardOutputIsJournal: true);

        announcer.PrintToStandardOutput("01JABCDEFGHJKMNPQRSTVWXYZ").ShouldBeFalse();

        output.ToString().ShouldBeEmpty();
    }
}
