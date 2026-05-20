namespace Collabhost.Api.Mcp;

// Scoped holder for the X-User-Key header value extracted at MCP session-setup time.
// Used as a fallback by McpRequestAuthenticator when a tool call omits the per-call
// authKey argument. Backward compatibility for v1.0.x clients that authenticate via
// the X-User-Key header at connection time -- see McpAuthentication.ConfigureSessionAsync.
public class McpHeaderFallback
{
    public string? HeaderKey { get; private set; }

    public void Set(string? headerKey) => HeaderKey = headerKey;
}
