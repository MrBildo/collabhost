namespace Collabhost.Api.Proxy;

// Scrubs the reverse-proxy vendor name out of an operator-facing error string before it
// crosses the /api/v1/status contract boundary as proxyDetail.lastSyncError (Card #426,
// FE-XT-03). Per CLAUDE.md the frontend must NOT translate vendor names -- the moment a
// value crosses into the frontend it is "Proxy" -- so the abstraction has to hold on the
// backend side of the contract, not in the renderer.
//
// The scrub is deliberately a whole-word "caddy" -> "Proxy" replacement (case-insensitive):
// it neutralizes the leak in every shape that can reach LastSyncError -- FormatSyncError's
// "Caddy admin API ..." prefixes, CaddyClient's "... contacting Caddy admin API" timeout
// string, a raw HttpRequestException message, and the vendor's own response body -- WITHOUT
// flattening the diagnostic. The HTTP status code, the bind/parse/issuer detail, and the
// rest of Caddy's response body all survive verbatim, so the operator still sees the real
// cause. Word-bounded so an unrelated substring is never mangled.
//
// Scope note: this is NOT a blanket rename. Backend internals and server log lines keep
// naming Caddy directly (CLAUDE.md permits it); the rule binds only where a value becomes
// frontend / user-facing. This helper is the single seam where the surfaced sync error
// crosses that line.
public static partial class ProxyErrorSanitizer
{
    private const string _vendorReplacement = "Proxy";

    [GeneratedRegex(@"\bcaddy\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex VendorNamePattern { get; }

    public static string? Sanitize(string? error) =>
        string.IsNullOrEmpty(error)
            ? error
            : VendorNamePattern.Replace(error, _vendorReplacement);
}
