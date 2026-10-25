namespace Collabhost.Api.Mcp;

// Shared parameter description for the per-call authKey argument carried by every MCP tool.
// One constant, so a wording change reaches every tool's schema description at once instead
// of drifting tool by tool. This text is sent to every connected MCP client, so it names
// nothing an operator outside this project could not look up.
internal static class McpAuthDescriptions
{
    public const string AuthKey =
        "Your Collabhost user key (ULID). It authenticates this call and records you as the "
        + "actor in the activity log. Pass your own key on every call: one MCP server "
        + "configuration is often shared by several agents, and the per-call key is how each "
        + "agent's actions are attributed to that agent. Read it from your own secret store "
        + "(an .env file, a secrets manager, or your agent's configuration). Optional only for "
        + "backward compatibility: if omitted, the server falls back to the X-User-Key header "
        + "set on the MCP connection. Treat as a secret -- it is the same key that authorizes "
        + "the Collabhost REST control plane.";
}
