using Collabhost.Api.Authorization;

using Microsoft.Extensions.Options;

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

        var user = await ResolveUserAsync(httpContext, authKey, ct);

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

    private static async Task<User?> ResolveUserAsync
    (
        HttpContext httpContext,
        string authKey,
        CancellationToken ct
    )
    {
        var settings = httpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<AuthorizationSettings>>()
            .CurrentValue;

        var userStore = httpContext.RequestServices.GetRequiredService<UserStore>();

        // Config key bypass: permanent lockout override
        if (settings.AdminKey is not null && authKey == settings.AdminKey)
        {
            var user = await userStore.GetByAuthKeyAsync(authKey, ct);

            if (user is not null)
            {
                return user;
            }

            // Config key always works even if no matching user exists in DB
            var logger = httpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(McpAuthentication));

            logger.LogWarning
            (
                "MCP auth bypass: request authenticated via config admin key with no matching DB user"
            );

            return new User
            {
                Name = "Admin (config bypass)",
                AuthKey = authKey,
                Role = UserRole.Administrator,
            };
        }

        return await userStore.GetByAuthKeyAsync(authKey, ct);
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
