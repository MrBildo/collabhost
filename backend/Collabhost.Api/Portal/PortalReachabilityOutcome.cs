namespace Collabhost.Api.Portal;

public enum PortalReachabilityStatus
{
    Ok,
    Missing,
    AssetsEmpty
}

public record PortalReachabilityOutcome
(
    PortalReachabilityStatus Status,
    string WwwrootPath,
    IReadOnlyList<string> RecoverySteps
);
