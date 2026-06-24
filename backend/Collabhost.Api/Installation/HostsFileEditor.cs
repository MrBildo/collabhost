using System.Globalization;

namespace Collabhost.Api.Installation;

// Pure marker-block rewriter for the hosts file. Reads the current file (if any), splices in or
// replaces a "# BEGIN COLLABHOST" ... "# END COLLABHOST" block, and atomically writes the result.
// No I/O beyond the supplied path. Card #345.
//
// Idempotency contract: callers pass the freshly-computed block content (the lines BETWEEN the
// markers, no trailing newline); RewriteAsync detects byte-identical existing content and
// short-circuits without touching the file -- avoiding an mtime bump that some Linux file-watch
// consumers would otherwise interpret as a change. When markers are absent, a fresh block is
// appended to the end of the file with a preceding blank line. When exactly one marker is
// present (operator hand-edit corruption), the orphan is left in place and a fresh block is
// appended; the orphan is a valid hosts-file comment and does not break resolution.
public static class HostsFileEditor
{
    // Full BEGIN-marker line emitted on writes. ExtractBlockBody anchors on the BeginMarker
    // string's prefix-up-to-"COLLABHOST" so an operator-tweaked suffix still matches.
    public const string BeginMarker = "# BEGIN COLLABHOST -- managed by 'collabhost --update-hosts', do not edit between markers";
    public const string EndMarker = "# END COLLABHOST";

    // Anchored prefix used to locate the BEGIN line regardless of the suffix the operator might
    // have tweaked. Public so tests reference the same string.
    public const string BeginMarkerPrefix = "# BEGIN COLLABHOST";

    public enum RewriteOutcome
    {
        // Block content was byte-identical -- no file touch, no mtime bump.
        NoChange,

        // File had an existing block; its contents were replaced.
        Replaced,

        // File had no block; a fresh block was appended.
        Appended,

        // File had exactly one marker (BEGIN or END but not both); orphan left in place, fresh
        // block appended. Caller may want to surface this as a warning.
        AppendedWithOrphan
    }

    // Rewrite the marker block in `hostsPath` to wrap `blockBody`. Returns the outcome plus a
    // warning string when the orphan path was taken.
    public static RewriteResult Rewrite(string hostsPath, string blockBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostsPath);
        ArgumentNullException.ThrowIfNull(blockBody);

        var existing = File.Exists(hostsPath)
            ? File.ReadAllText(hostsPath)
            : string.Empty;

        var lineEnding = DetectLineEnding(existing);

        var existingBlock = ExtractBlockBody(existing, out var blockSpan);

        if (existingBlock is not null && string.Equals(existingBlock, blockBody, StringComparison.Ordinal))
        {
            return new RewriteResult(RewriteOutcome.NoChange, null);
        }

        string newContent;
        RewriteOutcome outcome;
        string? warning = null;

        if (blockSpan is not null)
        {
            // Replace the existing block content between the markers, in place.
            newContent = ReplaceBlock(existing, blockSpan, blockBody, lineEnding);
            outcome = RewriteOutcome.Replaced;
        }
        else
        {
            var hasOrphanBegin = existing.Contains(BeginMarkerPrefix, StringComparison.Ordinal);
            var hasOrphanEnd = existing.Contains(EndMarker, StringComparison.Ordinal);

            if (hasOrphanBegin || hasOrphanEnd)
            {
                // INS-01: an orphan marker (a BEGIN or END without its complete pair) means
                // ExtractBlockBody could not find a block to replace, so the append path runs.
                // Appending blindly accumulates: an external editor that strips the END marker
                // between runs leaves the previous run's orphan BEGIN in place, and each run adds
                // another fresh block (BEGIN-count 1 -> 2 -> 3 -> ...). Reconcile first -- strip
                // EVERY Collabhost marker artifact (orphan markers and the lines of any partial
                // block) from the existing content, then append one canonical block. Repeated runs
                // now converge to exactly one block regardless of external marker corruption.
                var reconciled = StripMarkerArtifacts(existing);
                newContent = AppendBlock(reconciled, blockBody, lineEnding);
                outcome = RewriteOutcome.AppendedWithOrphan;
                warning = "Hosts file contained an orphan Collabhost marker (one of BEGIN/END "
                    + "without its pair). Reconciled the file to a single managed block; any "
                    + "stray marker lines were removed.";
            }
            else
            {
                newContent = AppendBlock(existing, blockBody, lineEnding);
                outcome = RewriteOutcome.Appended;
            }
        }

        TryWriteAtomic(hostsPath, newContent);

        return new RewriteResult(outcome, warning);
    }

    // Compose the marker-block lines from the supplied hostname rows. Each row is "<ip>\t<host>"
    // joined by the platform-native newline. Callers pass this through to Rewrite as `blockBody`.
    // The body intentionally has NO trailing newline -- the marker that follows it carries its
    // own.
    public static string ComposeBlockBody
    (
        IReadOnlyList<HostsFileEntry> entries,
        string lineEnding
    )
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var lines = new string[entries.Count];

        for (var i = 0; i < entries.Count; i++)
        {
            lines[i] = string.Format
            (
                CultureInfo.InvariantCulture,
                "{0}\t{1}",
                entries[i].IpAddress,
                entries[i].Hostname
            );
        }

        return string.Join(lineEnding, lines);
    }

    // Render the full marker block (BEGIN line + body + END line) for --dry-run output.
    public static string ComposeFullBlock(string blockBody, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(blockBody);
        ArgumentException.ThrowIfNullOrEmpty(lineEnding);

        return string.IsNullOrEmpty(blockBody)
            ? string.Concat(BeginMarker, lineEnding, EndMarker)
            : string.Concat(BeginMarker, lineEnding, blockBody, lineEnding, EndMarker);
    }

    // CRLF on Windows-default, LF on POSIX. When the file already has content, infer from the
    // first line ending seen. Caller passes the resolved line ending to ComposeBlockBody so the
    // emitted block matches the file's existing convention.
    public static string DetectLineEnding(string fileContent)
    {
        ArgumentNullException.ThrowIfNull(fileContent);

        var crlfIndex = fileContent.IndexOf("\r\n", StringComparison.Ordinal);
        var lfIndex = fileContent.IndexOf('\n', StringComparison.Ordinal);

        return crlfIndex >= 0 && (lfIndex == -1 || crlfIndex <= lfIndex)
            ? "\r\n"
            : lfIndex >= 0
                ? "\n"
                : OperatingSystem.IsWindows() ? "\r\n" : "\n";
    }

    // BlockSpan identifies the BEGIN-marker line and END-marker line offsets within the source
    // text. The body lives between them (exclusive of both markers' newlines).
    private sealed record BlockSpan(int BeginLineStart, int BodyStart, int BodyEnd, int EndLineEnd);

    private static string? ExtractBlockBody(string content, out BlockSpan? span)
    {
        span = null;

        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        // Find a "# BEGIN COLLABHOST" line. Anchor on the prefix so an operator-tweaked rationale
        // suffix on the BEGIN line still matches. Must be at the start of a line.
        var beginIndex = FindLineStartingWith(content, BeginMarkerPrefix, 0);

        if (beginIndex < 0)
        {
            return null;
        }

        var beginLineEnd = content.IndexOf('\n', beginIndex);

        if (beginLineEnd < 0)
        {
            return null;
        }

        var bodyStart = beginLineEnd + 1;

        var endIndex = FindLineStartingWith(content, EndMarker, bodyStart);

        if (endIndex < 0)
        {
            return null;
        }

        // bodyEnd points at the character just before the END line; strip the trailing newline
        // that sits between the body and the END line (CRLF or LF).
        var bodyEnd = endIndex;

        if (bodyEnd > bodyStart && content[bodyEnd - 1] == '\n')
        {
            bodyEnd--;

            if (bodyEnd > bodyStart && content[bodyEnd - 1] == '\r')
            {
                bodyEnd--;
            }
        }

        var endLineEnd = content.IndexOf('\n', endIndex);

        endLineEnd = endLineEnd < 0 ? content.Length : endLineEnd + 1;

        span = new BlockSpan(beginIndex, bodyStart, bodyEnd, endLineEnd);

        return content[bodyStart..bodyEnd];
    }

    // INS-01: remove every Collabhost marker artifact from the content -- standalone orphan BEGIN
    // and END marker lines, and the body lines of any partial BEGIN..END region (a BEGIN with a
    // later END, but not a clean pair ExtractBlockBody would have matched, e.g. a duplicate-BEGIN
    // tangle). Operator host entries outside any marker region are preserved exactly. The caller
    // appends one canonical block to the result, so repeated runs converge to a single block.
    private static string StripMarkerArtifacts(string content)
    {
        var lines = content.Split('\n');
        var kept = new List<string>(lines.Length);
        var insideRegion = false;

        foreach (var rawLine in lines)
        {
            // Compare against the line without a trailing CR so CRLF files match the LF-anchored markers.
            var line = rawLine.EndsWith('\r') ? rawLine[..^1] : rawLine;

            if (line.StartsWith(BeginMarkerPrefix, StringComparison.Ordinal))
            {
                // Enter a region: drop this BEGIN line and everything up to (and including) the next END.
                insideRegion = true;
                continue;
            }

            if (line.StartsWith(EndMarker, StringComparison.Ordinal))
            {
                // Drop the END line; if it was a region close, exit the region. An orphan END with
                // no preceding BEGIN simply gets dropped (insideRegion was already false).
                insideRegion = false;
                continue;
            }

            if (insideRegion)
            {
                // Partial-block body between an orphan BEGIN and a later END -- drop it.
                continue;
            }

            kept.Add(rawLine);
        }

        return string.Join('\n', kept);
    }

    private static int FindLineStartingWith(string content, string prefix, int searchFrom)
    {
        var index = searchFrom;

        while (index < content.Length)
        {
            var found = content.IndexOf(prefix, index, StringComparison.Ordinal);

            if (found < 0)
            {
                return -1;
            }

            if (found == 0 || content[found - 1] == '\n')
            {
                return found;
            }

            index = found + 1;
        }

        return -1;
    }

    private static string ReplaceBlock(string content, BlockSpan span, string newBody, string lineEnding)
    {
        // Preserve everything outside the BEGIN ... END lines exactly; rewrite the body between
        // them. Markers themselves are re-emitted from constants so an operator-tweaked BEGIN
        // line's suffix is normalized back to canonical on every sync.
        var before = content[..span.BeginLineStart];
        var after = content[span.EndLineEnd..];

        var replacement = string.IsNullOrEmpty(newBody)
            ? string.Concat(BeginMarker, lineEnding, EndMarker, lineEnding)
            : string.Concat(BeginMarker, lineEnding, newBody, lineEnding, EndMarker, lineEnding);

        // If the original block was the last thing in the file with no trailing newline, drop
        // the trailing newline we just added so we don't introduce a blank line that wasn't
        // there before.
        if (after.Length == 0 && replacement.EndsWith(lineEnding, StringComparison.Ordinal))
        {
            replacement = replacement[..^lineEnding.Length];
        }

        return string.Concat(before, replacement, after);
    }

    private static string AppendBlock(string content, string blockBody, string lineEnding)
    {
        var block = ComposeFullBlock(blockBody, lineEnding);

        if (string.IsNullOrEmpty(content))
        {
            return string.Concat(block, lineEnding);
        }

        // Ensure exactly one blank line between existing content and the new block. Strip
        // trailing newlines first, then add: <newline><blank line><block><newline>.
        var trimmed = content;

        while (trimmed.EndsWith('\n') || trimmed.EndsWith('\r'))
        {
            trimmed = trimmed[..^1];
        }

        return string.Concat(trimmed, lineEnding, lineEnding, block, lineEnding);
    }

    private static void TryWriteAtomic(string path, string content)
    {
        // Mirrors AppSettingsMergeCli.TryWriteAtomic. File.Move(overwrite=true) is atomic on the
        // same volume on POSIX (rename(2)) and Windows (NTFS rename). The hosts file is on the
        // system volume by definition, so the temp file lands on the same volume.
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup. A stray .tmp file is annoying but harmless.
            }

            throw;
        }
    }

    public sealed record RewriteResult(RewriteOutcome Outcome, string? Warning);
}

public sealed record HostsFileEntry(string IpAddress, string Hostname);
