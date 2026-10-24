namespace Collabhost.Api.Platform;

// Browser-hardening headers on every response Collabhost itself serves. The dashboard keeps an admin
// key in localStorage, and every hosted app answers on a sibling subdomain (the same site), so a
// page on any hosted app could otherwise frame a logged-in dashboard and steer an operator's clicks.
// Set here, in the app, so the direct port and the proxy-fronted dashboard hostname both carry them
// (the proxy passes upstream headers through). A full Content-Security-Policy belongs here too once
// the dashboard's fonts are served locally; until then the policy carries frame-ancestors only.
public static class ResponseSecurityHeaders
{
    public const string FrameOptions = "DENY";

    public const string ContentSecurityPolicy = "frame-ancestors 'none'";

    public const string ContentTypeOptions = "nosniff";

    public const string ReferrerPolicy = "no-referrer";

    // Applied when the response starts rather than when the request arrives, so a handler or error
    // path that clears the response before writing it cannot drop them.
    public static Task ApplyAsync(object state)
    {
        var headers = ((HttpResponse)state).Headers;

        headers.XFrameOptions = FrameOptions;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = ContentTypeOptions;
        headers["Referrer-Policy"] = ReferrerPolicy;

        return Task.CompletedTask;
    }
}
