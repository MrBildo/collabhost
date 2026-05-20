namespace Collabhost.Api.Mcp;

// Shared parameter descriptions for the per-call authKey argument carried by every MCP
// tool (Card #332). Centralized so wording changes propagate consistently to every tool's
// schema description without per-tool drift.
internal static class McpAuthDescriptions
{
    public const string AuthKey =
        "Your Collabhost user key (ULID) used to authenticate this call and stamp activity-log "
        + "actor attribution. This is the blessed per-bot identity channel through a shared "
        + "user-scope MCP server; each bot supplies its own key from its own per-bot config "
        + "(e.g. COLLABHOST_AUTH_KEY in ~/.agents/bots/<bot>/.env). Optional only for backward "
        + "compatibility: if omitted, the server falls back to the X-User-Key header captured "
        + "at MCP session setup. Treat as a secret -- it is the same key that authorizes the "
        + "Collabhost REST control plane.";
}
