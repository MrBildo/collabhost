namespace Collabhost.Api.Platform;

// Singleton service that captures the host process start time from
// IHostApplicationLifetime.ApplicationStarted. Registered by PlatformRegistration.AddPlatform().
//
// Singleton-registered; the IHostApplicationLifetime reference is held for the lifetime
// of the application so the callback registration stays alive.
public sealed class ApplicationStartTime : IApplicationStartTime
{
    // Fallback in case UtcStartedAt is read before ApplicationStarted fires -- DateTime.MinValue
    // produces a large positive uptime rather than 0/-0, making it obvious something is wrong
    // rather than silently misleading. In practice this should not occur: the DI container is
    // fully resolved before any endpoint can be reached, and ApplicationStarted fires as part
    // of WebApplication.RunAsync before the server accepts connections.
    public DateTime UtcStartedAt { get; private set; } = DateTime.MinValue;

    public ApplicationStartTime(IHostApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        lifetime.ApplicationStarted.Register(Capture);
    }

    private void Capture() => UtcStartedAt = DateTime.UtcNow;
}
