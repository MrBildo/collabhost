namespace Collabhost.Api.Platform;

// Shared source-of-truth for the host process start time. Captured from
// IHostApplicationLifetime.ApplicationStarted -- guaranteed to fire after DI is
// fully wired and Kestrel has bound, which means the first inbound request cannot
// arrive before the snapshot is taken. This eliminates the 0 / -0 race on
// uptimeSeconds that occurs when a static readonly field (set at JIT-init of the
// class, not at process-start) is subtracted from DateTime.UtcNow within ~500ms
// of the first class-touch. Card #222.
public interface IApplicationStartTime
{
    // UTC timestamp at which IHostApplicationLifetime.ApplicationStarted fired.
    // UtcKind is guaranteed.
    DateTime UtcStartedAt { get; }
}
