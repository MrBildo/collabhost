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
//
// Card #371 -- two guards make the fallback path loud rather than silent:
//   * Conflict guard: when a per-call authKey AND a session X-User-Key both resolve to
//     *different* active User entities, the call is refused. Letting one silently win is how
//     an MCP call gets attributed to the wrong bot in the activity log (#370). A caller who
//     supplies a single credential -- authKey only, or header only -- is unaffected.
//   * Fallback-visibility: any time the header fallback is actually used to authenticate
//     (no per-call authKey supplied), a warning is logged so the silent path is visible in
//     operator logs.
public class McpRequestAuthenticator
(
    AuthKeyResolver authKeyResolver,
    CurrentUser currentUser,
    McpHeaderFallback headerFallback,
    ILogger<McpRequestAuthenticator> logger
)
{
    private readonly AuthKeyResolver _authKeyResolver = authKeyResolver
        ?? throw new ArgumentNullException(nameof(authKeyResolver));

    private readonly CurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly McpHeaderFallback _headerFallback = headerFallback
        ?? throw new ArgumentNullException(nameof(headerFallback));

    private readonly ILogger<McpRequestAuthenticator> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<CallToolResult?> AuthenticateAsync
    (
        string? authKey,
        string toolName,
        CancellationToken ct
    )
    {
        var hasCallKey = !string.IsNullOrWhiteSpace(authKey);
        var headerKey = _headerFallback.HeaderKey;
        var hasHeaderKey = !string.IsNullOrWhiteSpace(headerKey);

        if (!hasCallKey && !hasHeaderKey)
        {
            return Unauthorized
            (
                "Authentication required. Provide an authKey argument (your per-bot Collabhost "
                + "user key) or set the X-User-Key header at MCP connection time. "
                + "See INSTALL.md § MCP client setup."
            );
        }

        // Conflict guard (#371): when both credentials are present and resolve to different
        // active users, refuse rather than silently letting the per-call key win. Resolving
        // both is what surfaces the ambiguity -- a mismatch here is the exact shape that
        // mis-attributed an MCP call to the wrong bot in #370. Identical key strings cannot
        // disagree, so skip the double-resolve in that (common) case.
        if (hasCallKey
            && hasHeaderKey
            && !string.Equals(authKey, headerKey, StringComparison.Ordinal))
        {
            var callUser = await _authKeyResolver.ResolveAsync(authKey!, ct);
            var headerUser = await _authKeyResolver.ResolveAsync(headerKey!, ct);

            if (CredentialsConflict(callUser, headerUser))
            {
                _logger.LogWarning
                (
                    "MCP auth: per-call authKey and session X-User-Key resolve to different "
                    + "users for tool {ToolName}. Refusing to guess identity.",
                    toolName
                );

                return Unauthorized
                (
                    "Conflicting credentials: the per-call authKey and the session X-User-Key "
                    + "resolve to different users. Supply only your own per-call authKey, or "
                    + "remove the pinned X-User-Key header so the two cannot disagree."
                );
            }
        }

        // Per-call key takes precedence; fall back to the header captured at session setup.
        var usingHeaderFallback = !hasCallKey;
        var effectiveKey = hasCallKey ? authKey : headerKey;

        var user = await _authKeyResolver.ResolveAsync(effectiveKey!, ct);

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

        // Fallback-visibility (#371): a call that authenticated purely off the pinned header
        // (no per-call authKey) is the silent path that mis-attributed #370. Log it so the
        // fallback's use is visible to operators reading the trail, without breaking the
        // documented v1.0.x backward-compat contract.
        if (usingHeaderFallback)
        {
            _logger.LogWarning
            (
                "MCP auth: tool {ToolName} authenticated via the X-User-Key session header "
                + "fallback (no per-call authKey supplied). Activity is attributed to user "
                + "{UserName} ({UserId}). Supply a per-call authKey to attribute reliably.",
                toolName,
                user.Name,
                user.Id
            );
        }

        _currentUser.Set(user);

        return null;
    }

    // Two resolved credentials conflict when both resolve to a user but to different user
    // identities. An unresolved key (null) does not conflict -- it falls through to the normal
    // invalid-key path. Identity is compared by User.Id. The caller has already excluded the
    // identical-key-string case, so the config-admin-key bypass (which mints a transient admin
    // per call) is only reached here when the two key strings genuinely differ -- a real
    // conflict, correctly refused.
    private static bool CredentialsConflict(User? callUser, User? headerUser) =>
        callUser is not null
            && headerUser is not null
            && callUser.Id != headerUser.Id;

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
