namespace Collabhost.Api.Authorization;

public static class Entitlements
{
    public static bool CanAccessTool(UserRole role, string toolName) => role switch
    {
        UserRole.Administrator => true,
        UserRole.Agent => _agentTools.Contains(toolName),
        _ => false
    };

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
        "list_events",
    ];
}
