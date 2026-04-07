namespace Collabhost.Api.Authorization;

public static class Entitlements
{
    // REST endpoints restricted to Administrator only
    public static readonly IReadOnlySet<string> AdminOnlyEndpointPrefixes = new HashSet<string>
    (
        ["/api/v1/users"],
        StringComparer.OrdinalIgnoreCase
    );

    public static bool CanAccessTool(UserRole role, string toolName) => role switch
    {
        UserRole.Administrator => true,
        UserRole.Agent => _agentTools.Contains(toolName),
        _ => false
    };

    public static bool CanAccessEndpoint(UserRole role, string path)
    {
        if (role == UserRole.Administrator)
        {
            return true;
        }

        foreach (var prefix in AdminOnlyEndpointPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly HashSet<string> _agentTools =
    [
        "get_system_status",
        "list_apps",
        "get_app",
        "list_app_types",
        "start_app",
        "stop_app",
        "restart_app",
        "kill_app",
        "get_logs",
        "get_settings",
        "update_settings",
        "list_routes",
        "reload_proxy",
        "register_app",
        "browse_filesystem",
        "detect_strategy",
    ];
}
