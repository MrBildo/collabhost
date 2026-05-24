namespace Collabhost.Api.Portal;

public enum PortalIntegrityStatus
{
    Ok,
    Drift,
    Unknown
}

public record PortalIntegrityOutcome
(
    PortalIntegrityStatus Status,
    string WwwrootPath,
    string ExpectedHash,
    string ActualHash,
    IReadOnlyList<string> RecoverySteps
);
