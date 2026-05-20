using Collabhost.Api.Authorization;

using ModelContextProtocol.Protocol;

namespace Collabhost.Api.Mcp;

// Per-call MCP authentication. Each MCP tool invocation calls AuthenticateAsync at the top
// of its body. The authenticator:
//   1. Resolves the auth key from the per-call `authKey` argument; falls back to the
//      X-User-Key header captured at session setup (backward compat for v1.0.x clients).
//   2. Resolves the User via AuthKeyResolver.
//   3. Enforces per-tool entitlement (Entitlements.CanAccessTool).
//   4. Seeds the scoped CurrentUser so activity-log actor stamping works.
//   5. Returns null on success, or an error CallToolResult on failure (missing/invalid key,
//      deactivated user, role not entitled).
//
// Why per-call: Card #332. When a single user-scope MCP server is shared across multiple bots,
// a static shared X-User-Key header yields one shared identity for every caller. Per-call
// authKey is the only channel through which per-bot identity enters a shared server. See the
// card body and comment thread for the full rationale.
//
// The header-fallback path is preserved so existing v1.0.x clients that pin X-User-Key in a
// per-bot .mcp.json (or any other instance-local config) keep working without modification.
// A per-call authKey overrides the header when both are present.
public class McpRequestAuthenticator
(
    AuthKeyResolver authKeyResolver,
    CurrentUser currentUser,
    McpHeaderFallback headerFallback
)
{
    private readonly AuthKeyResolver _authKeyResolver = authKeyResolver
        ?? throw new ArgumentNullException(nameof(authKeyResolver));

    private readonly CurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly McpHeaderFallback _headerFallback = headerFallback
        ?? throw new ArgumentNullException(nameof(headerFallback));

    public async Task<CallToolResult?> AuthenticateAsync
    (
        string? authKey,
        string toolName,
        CancellationToken ct
    )
    {
        // Per-call key takes precedence; fall back to the header captured at session setup.
        var effectiveKey = !string.IsNullOrWhiteSpace(authKey)
            ? authKey
            : _headerFallback.HeaderKey;

        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            return Unauthorized
            (
                "Authentication required. Provide an authKey argument (your per-bot Collabhost "
                + "user key) or set the X-User-Key header at MCP connection time. "
                + "See INSTALL.md § MCP client setup."
            );
        }

        var user = await _authKeyResolver.ResolveAsync(effectiveKey, ct);

        if (user is null || !user.IsActive)
        {
            return Unauthorized("Invalid or deactivated authKey.");
        }

        if (!Entitlements.CanAccessTool(user.Role, toolName))
        {
            return Forbidden
            (
                $"Tool '{toolName}' is not available to role '{user.Role}'. "
                + "Contact an administrator if you need elevated access."
            );
        }

        _currentUser.Set(user);

        return null;
    }

    private static CallToolResult Unauthorized(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true
    };

    private static CallToolResult Forbidden(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true
    };
}
