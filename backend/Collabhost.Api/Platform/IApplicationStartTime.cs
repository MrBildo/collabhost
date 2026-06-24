namespace Collabhost.Api.Platform;

// Shared source-of-truth for platform (this-process) start time. The epoch is the OS-recorded
// process start time, so platform uptime is monotonic, never reads 0, and is always >= any
// managed app's uptime (a supervised child cannot be older than the platform supervising it).
// Card #408 (subsumes the #222 0/-0 race -- see ApplicationStartTime for the full rationale).
public interface IApplicationStartTime
{
    // UTC timestamp of this process's OS-recorded start time. Kind is guaranteed Utc.
    DateTime UtcStartedAt { get; }
}
