using Microsoft.Net.Http.Headers;

namespace Collabhost.Api.Portal;

// SPA fallback for the embedded React dashboard. Runs in the middleware phase BEFORE auth
// so that React Router client-side paths (/apps, /users, /system, /apps/foo, ...) reach the
// browser as the SPA shell HTML rather than a 401 from the auth wall. Auth is enforced at
// API-call time by <AuthGate> calling /api/v1/auth/me; serving index.html to an
// unauthenticated browser reveals no app data.
//
// The middleware short-circuits the pipeline when ALL of the following hold:
//   - Method is GET or HEAD.
//   - Client accepts HTML (header absent, or any parsed media type matches text/html).
//   - Path does not start with one of the non-Portal subsystem prefixes
//     (/api, /health, /alive, /openapi, /mcp). Mirrors AuthorizationMiddleware._skipPrefixes
//     plus /api so API requests still reach the auth wall.
//   - wwwroot/index.html exists on disk. If absent (packaging regression, stripped
//     deployment), the middleware passes through and auth runs as today; the
//     PortalReachabilityCheck has already warned at boot.
public class PortalSpaFallbackMiddleware
(
    RequestDelegate next,
    IWebHostEnvironment environment
)
{
    private readonly RequestDelegate _next = next
        ?? throw new ArgumentNullException(nameof(next));

    private readonly IWebHostEnvironment _environment = environment
        ?? throw new ArgumentNullException(nameof(environment));

    private static readonly string[] _excludedPathPrefixes =
    [
        "/api",
        "/health",
        "/alive",
        "/openapi",
        "/mcp"
    ];

    private static readonly MediaTypeHeaderValue _textHtml = MediaTypeHeaderValue.Parse("text/html");

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldServeShell(context, _environment, out var indexHtmlPath))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";

        if (HttpMethods.IsHead(context.Request.Method))
        {
            // No body for HEAD per RFC 9110 §9.3.2; status + headers only.
            return;
        }

        await context.Response.SendFileAsync(indexHtmlPath, context.RequestAborted);
    }

    private static bool ShouldServeShell
    (
        HttpContext context,
        IWebHostEnvironment environment,
        out string indexHtmlPath
    )
    {
        indexHtmlPath = string.Empty;

        var method = context.Request.Method;

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        foreach (var prefix in _excludedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (!ClientAcceptsHtml(context.Request))
        {
            return false;
        }

        // Resolve wwwroot/index.html through the host environment's file provider so
        // tests that inject a temporary WebRootPath are honored. The file provider
        // returns a non-existent IFileInfo when the file is absent rather than throwing.
        var indexFile = environment.WebRootFileProvider.GetFileInfo("index.html");

        if (!indexFile.Exists || indexFile.IsDirectory || indexFile.PhysicalPath is null)
        {
            return false;
        }

        indexHtmlPath = indexFile.PhysicalPath;
        return true;
    }

    private static bool ClientAcceptsHtml(HttpRequest request)
    {
        // Absent Accept: serve the shell. Captures "any client that would render HTML gets HTML."
        if (!request.Headers.TryGetValue(HeaderNames.Accept, out var acceptHeader))
        {
            return true;
        }

        if (!MediaTypeHeaderValue.TryParseList(acceptHeader, out var parsed))
        {
            // Malformed Accept header: fall through to auth rather than serve the shell.
            return false;
        }

        // MatchesMediaType is content-negotiation aware: text/html, */*, and text/* all match.
        // application/json does not.
        foreach (var mediaType in parsed)
        {
            if (mediaType.MatchesMediaType(_textHtml.MediaType))
            {
                return true;
            }
        }

        return false;
    }
}
