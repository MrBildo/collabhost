namespace Collabhost.Api.Mcp;

public static class McpServerInstructions
{
    public static string Content { get; } = """
        Collabhost -- self-hosted application platform managing local services and workers.

        Apps are identified by slug (lowercase, hyphenated, e.g. 'my-api-server').
        Five app types: dotnet-app, nodejs-app, static-site, executable, system-service.
        Static sites toggle Caddy routing on start/stop -- no process involved.
        Each running app gets a route: {slug}.collab.internal.

        Workflows:
        - Discovery: list_apps first to get slugs, then get_app for full details including
          technology probes (runtime, frameworks, dependencies).
        - Lifecycle: start_app/stop_app return immediately. Poll get_app to confirm status.
          Use restart_app for a stop-then-start cycle. Always try stop_app before kill_app.
        - Registration: list_app_types (includes schemas) -> browse_filesystem ->
          detect_strategy -> register_app -> start_app.
        - Configuration: get_settings then update_settings (read-then-write). Some settings
          require restart.
        - Diagnostics: get_logs for process output (token-limited, summarize findings),
          list_routes for proxy state, reload_proxy if routes are stale.

        Destructive: delete_app is permanent and cannot be undone. Verify with the user first.

        Log responses are token-limited; use limit and offset parameters for pagination.
        Do not dump full log contents to the user -- summarize key findings.

        System info: get_system_status returns hostname, version, and uptime.
        """;
}
