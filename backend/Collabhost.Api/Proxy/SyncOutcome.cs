namespace Collabhost.Api.Proxy;

// The result of the most recent route-sync attempt. Surfaced via /api/v1/status
// as the proxyDetail object so operators can see *why* the proxy is degraded
// (bind failure, config rejection, transport timeout) without reading server logs.
// Card #217.
//
// Sealed: this is a value-shape carrier consumed by ProxyManager + the status
// endpoint mapping; subclassing would just fragment the contract.
public sealed record SyncOutcome
(
    bool Attempted,
    bool Success,
    DateTime? LastSyncAt,
    string? ErrorMessage
)
{
    public static SyncOutcome NeverAttempted { get; } = new
    (
        Attempted: false,
        Success: false,
        LastSyncAt: null,
        ErrorMessage: null
    );

    public static SyncOutcome Succeeded(DateTime at) =>
        new(Attempted: true, Success: true, LastSyncAt: at, ErrorMessage: null);

    // Sanitize the error at construction so the reverse-proxy vendor name never reaches
    // ErrorMessage -- the field is surfaced raw by the FE via /status's proxyDetail, and per
    // CLAUDE.md the abstraction must hold on the backend side of the contract. Scrubbing here
    // (not at the /status endpoint) keeps every producer of a failed outcome clean by
    // construction and survives a future second reader of ErrorMessage. Card #426 (FE-XT-03).
    public static SyncOutcome Failed(DateTime at, string errorMessage) =>
        new(Attempted: true, Success: false, LastSyncAt: at, ErrorMessage: ProxyErrorSanitizer.Sanitize(errorMessage));
}
