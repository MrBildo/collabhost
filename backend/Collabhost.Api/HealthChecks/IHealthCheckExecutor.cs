namespace Collabhost.Api.HealthChecks;

// Reader interface consumed by AppEndpoints. The executor hosted service polls each
// app's configured health-check endpoint at the configured interval; endpoints read
// the latest result on demand. A null return means "no probe has been performed yet"
// or "the capability is not enabled for this app." The frontend renders null as "--".
public interface IHealthCheckExecutor
{
    HealthCheckResult? GetLatest(Ulid appId);
}

public record HealthCheckResult
(
    HealthCheckStatus Status,
    DateTime LastCheckedAt,
    string? LastError
);

public enum HealthCheckStatus
{
    Unknown,
    Healthy,
    Unhealthy
}

public static class HealthCheckStatusExtensions
{
    extension(HealthCheckStatus status)
    {
        public string ToApiString() => status switch
        {
            HealthCheckStatus.Unknown => "unknown",
            HealthCheckStatus.Healthy => "healthy",
            HealthCheckStatus.Unhealthy => "unhealthy",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown HealthCheckStatus value"),
        };
    }
}
