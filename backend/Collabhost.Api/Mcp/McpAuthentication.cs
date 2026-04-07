using Collabhost.Api.Authorization;

using ModelContextProtocol.Server;

namespace Collabhost.Api.Mcp;

public static class McpAuthentication
{
    public static async Task ConfigureSessionAsync
    (
        HttpContext httpContext,
        McpServerOptions sessionOptions,
        CancellationToken ct
    )
    {
        var authKey = httpContext.Request.Headers["X-User-Key"].ToString();

        if (string.IsNullOrEmpty(authKey))
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsJsonAsync
            (
                new { error = "Unauthorized", message = "API key is required. Provide X-User-Key header." },
                ct
            );
            return;
        }

        var resolver = httpContext.RequestServices.GetRequiredService<AuthKeyResolver>();
        var user = await resolver.ResolveAsync(authKey, ct);

        if (user is null || !user.IsActive)
        {
            httpContext.Response.StatusCode = 401;
            await httpContext.Response.WriteAsJsonAsync
            (
                new { error = "Unauthorized", message = "Invalid or deactivated API key." },
                ct
            );
            return;
        }

        // Populate scoped ICurrentUser for tool methods to inject
        var currentUser = httpContext.RequestServices.GetRequiredService<CurrentUser>();
        currentUser.Set(user);

        // Filter tool visibility by role
        if (user.Role != UserRole.Administrator)
        {
            FilterToolsByRole(sessionOptions, user.Role);
        }
    }

    private static void FilterToolsByRole(McpServerOptions sessionOptions, UserRole role)
    {
        var allTools = sessionOptions.ToolCollection;

        if (allTools is null)
        {
            return;
        }

        var toolsToRemove = allTools
            .Where(tool => !Entitlements.CanAccessTool(role, tool.ProtocolTool.Name))
                .ToList();

        foreach (var tool in toolsToRemove)
        {
            allTools.Remove(tool);
        }
    }
}
