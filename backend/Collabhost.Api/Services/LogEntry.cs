namespace Collabhost.Api.Services;

public enum LogStream { StdOut, StdErr }

public record LogEntry(DateTime Timestamp, LogStream Stream, string Content);

