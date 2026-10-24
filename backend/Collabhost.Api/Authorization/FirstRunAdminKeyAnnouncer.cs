namespace Collabhost.Api.Authorization;

// Hands an admin key Collabhost generated on its first run to the operator, and to no log. The key
// never passes through ILogger: every logging provider is a sink that outlives the moment of capture
// and is readable by more people than the operator (the Windows Event Log, journald by way of the
// console logger under systemd, any telemetry exporter). Two channels instead:
//
//   - A file in the data directory, created readable and writable by its owner only on Linux and
//     macOS, so a headless service install -- where no terminal sees standard output -- can still
//     read the key. It sits beside collabhost.db, which holds the same key, so it exposes nothing the
//     database next to it doesn't; on Windows it takes the data directory's permissions, as the
//     database does.
//   - One line on standard output, for the terminal (or the file it was redirected to) the operator
//     launched from -- unless standard output is the systemd journal, where the line would be kept in
//     the journal. There it is left out, and the log records only where the file is.
public class FirstRunAdminKeyAnnouncer
(
    string dataDirectory,
    Func<TextWriter> standardOutput,
    bool standardOutputIsJournal
)
{
    public const string KeyFileName = "initial-admin-key.txt";

    // Operators and install checks find the line by this prefix; keep it stable.
    public const string StandardOutputMarker = "Collabhost admin key:";

    private readonly Func<TextWriter> _standardOutput = standardOutput
        ?? throw new ArgumentNullException(nameof(standardOutput));

    private readonly bool _standardOutputIsJournal = standardOutputIsJournal;

    public string KeyFilePath { get; } = Path.Combine
    (
        dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory)),
        KeyFileName
    );

    public void WriteKeyFile(string adminKey)
    {
        var path = KeyFilePath;

        // A file left by an earlier first run (the database was reset, the file was not) is replaced
        // rather than overwritten in place, so the permissions are the ones this create sets.
        File.Delete(path);

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);

        writer.WriteLine(adminKey);
    }

    // True when the key was printed; false when standard output is the journal and it was not.
    public bool PrintToStandardOutput(string adminKey)
    {
        if (_standardOutputIsJournal)
        {
            return false;
        }

        var output = _standardOutput();

        output.WriteLine($"{StandardOutputMarker} {adminKey}");
        output.Flush();

        return true;
    }

    public static bool StandardOutputIsJournal() =>
        OperatingSystem.IsLinux()
            && IsJournalStream(Environment.GetEnvironmentVariable("JOURNAL_STREAM"), ReadStandardOutputTarget());

    // systemd sets JOURNAL_STREAM to "<device>:<inode>" of the socket it connected to the journal, and
    // documents comparing that against the process's own output stream: the variable is inherited by
    // child processes whose output goes somewhere else (a terminal opened from a desktop session, a
    // CI job redirecting to a file). On Linux, /proc/self/fd/1 names a socket as "socket:[<inode>]".
    internal static bool IsJournalStream(string? journalStream, string? standardOutputTarget)
    {
        if (string.IsNullOrEmpty(journalStream) || string.IsNullOrEmpty(standardOutputTarget))
        {
            return false;
        }

        var separator = journalStream.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0)
        {
            return false;
        }

        var journalInode = journalStream[(separator + 1)..];

        return journalInode.Length > 0
            && journalInode.All(char.IsAsciiDigit)
            && string.Equals(standardOutputTarget, $"socket:[{journalInode}]", StringComparison.Ordinal);
    }

    private static string? ReadStandardOutputTarget()
    {
        try
        {
            return new FileInfo("/proc/self/fd/1").LinkTarget;
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
}
