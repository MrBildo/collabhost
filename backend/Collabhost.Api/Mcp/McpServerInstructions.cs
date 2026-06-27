namespace Collabhost.Api.Mcp;

public static class McpServerInstructions
{
    public static string Content { get; } = """
        Collabhost -- self-hosted application platform managing local services and workers.

        Apps are identified by slug (lowercase, hyphenated, e.g. 'my-api-server').
        Seven app types: dotnet-app, nodejs-app, static-site, executable, system-service, external-route, internal-service.
        Static sites toggle proxy routing on start/stop -- no process involved.
        external-route is the right type for services Collabhost does not run (Docker, LAN, Tailscale):
        register with external-target carrying the upstream host + port + optional scheme.
        internal-service is the right type for non-HTTP managed processes (databases, message brokers,
        custom-protocol services): Collabhost runs and supervises the process, no proxy route is generated.
        Each running app gets a route: {slug}.<configured-base-domain>.

        Authentication (Card #332):
        Every tool accepts an optional `authKey` parameter -- your per-bot Collabhost user key
        (ULID). This is the blessed mechanism for per-bot identity through a shared user-scope
        MCP server: each bot supplies its own key per call so activity-log actor attribution
        reflects the calling bot rather than a shared identity. Read your key from your own
        per-bot config (e.g. COLLABHOST_AUTH_KEY in ~/.agents/bots/<bot>/.env), symmetric to
        COLLABOARD_AUTH_KEY. The key is a secret -- it is the same credential that authorizes
        the Collabhost REST control plane (process start/kill, registration, settings, delete).
        Backward compatibility: if a client pinned X-User-Key in its MCP connection config,
        the server falls back to that header when authKey is omitted; the per-call argument
        is preferred and overrides the header when both are present.

        Workflows:
        - Discovery: list_apps first to get slugs, then get_app for full details including
          technology probes (runtime, frameworks, dependencies).
        - Lifecycle: start_app/stop_app return immediately. Poll get_app to confirm status.
          Use restart_app for a stop-then-start cycle. Always try stop_app before kill_app.
        - Registration: list_app_types (includes schemas) -> browse_filesystem ->
          detect_strategy -> register_app -> start_app. register_app and get_app
          return 'writableDataPath' -- a platform-provisioned absolute directory
          inside Collabhost's writable data root. Point any app that writes to
          disk (e.g. a SQLite connection string) at this path rather than its
          install directory, which may be read-only under a hardened deployment.
        - Configuration: get_settings then update_settings (read-then-write). Some settings
          require restart.
        - Diagnostics: get_logs for process output (token-limited, summarize findings),
          list_routes for proxy state, reload_proxy if routes are stale.

        Destructive: delete_app is permanent and cannot be undone. Requires an administrator
        authKey. Verify with the user first.

        Log responses are token-limited; use limit and offset parameters for pagination.
        Do not dump full log contents to the user -- summarize key findings.

        System info: get_system_status returns hostname, version, and uptime.
        """;
}
