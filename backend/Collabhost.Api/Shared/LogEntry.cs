namespace Collabhost.Api.Shared;

public enum LogStream
{
    StdOut,
    StdErr
}

public record LogEntry(DateTime Timestamp, LogStream Stream, string Content);
