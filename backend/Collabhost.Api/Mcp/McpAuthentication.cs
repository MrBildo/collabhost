using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

// MCP session setup. Permissive at the HTTP/session layer; per-call enforcement happens
// in each tool via McpRequestAuthenticator. See Card #332 for the rationale (per-bot
// identity through a shared user-scope MCP server requires per-call authKey).
//
// Backward compatibility path: clients that pinned X-User-Key in their .mcp.json under
// v1.0.x still have their header read here and stashed in McpHeaderFallback so tools
// can fall back to it when no per-call authKey is supplied. The 401 / role-filtering
// that used to live here moved to McpRequestAuthenticator -- session setup no longer
// rejects unauthenticated requests because the per-call authKey may still arrive.
public static class McpAuthentication
{
    // The McpServerOptions and CancellationToken parameters are part of the SDK delegate
    // shape (ConfigureSessionOptionsCallback). They are intentionally unused here -- session
    // setup no longer needs them under the per-call auth model.
#pragma warning disable IDE0060
    public static Task ConfigureSessionAsync
    (
        HttpContext httpContext,
        McpServerOptions sessionOptions,
        CancellationToken ct
    )
#pragma warning restore IDE0060
    {
        var headerKey = httpContext.Request.Headers["X-User-Key"].ToString();

        if (!string.IsNullOrEmpty(headerKey))
        {
            var fallback = httpContext.RequestServices.GetRequiredService<McpHeaderFallback>();
            fallback.Set(headerKey);
        }

        return Task.CompletedTask;
    }
}
