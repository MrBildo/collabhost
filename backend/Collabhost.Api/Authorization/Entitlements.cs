namespace Collabhost.Api.Authorization;

public static class Entitlements
{
    public static bool CanAccessTool(UserRole role, string toolName) => role switch
    {
        UserRole.Administrator => true,
        UserRole.Agent => _agentTools.Contains(toolName),
        UserRole.ReadOnly => _readOnlyTools.Contains(toolName),
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

    // The read-only observability tier: non-mutating reads that carry no secrets. A read-only
    // key answers "what exists and is it running" for status pages, external monitors, and
    // watcher automation without holding start/kill authority or secret visibility. Deliberately
    // excludes get_logs / list_events / get_settings (these can surface secrets -- env values,
    // log output, operational history) and every mutating tool. Narrower than the Agent set on
    // purpose: this is the least-privilege rung, and a deny-by-default tier is widened additively
    // far more safely than an over-grant is walked back.
    private static readonly HashSet<string> _readOnlyTools =
    [
        "get_system_status",
        "list_apps",
        "get_app",
        "list_app_types",
        "list_routes",
    ];
}
