namespace Collabhost.Api.Proxy;

// Proxy subsystem state surfaced externally via GET /api/v1/status.
// Lowercase wire form is produced at the endpoint boundary; internal code uses the enum.
public enum ProxyState
{
    // Default initial value until VerifyCaddyReady resolves. Covers the window between
    // ProxyManager.StartAsync and probe completion (<=5s).
    Starting = 0,

    // Caddy is up, admin API reachable, route sync is active.
    Running = 1,

    // Caddy was launched but the admin-API probe failed within the 5s budget.
    // Most operationally actionable state.
    Failed = 2,

    // No Caddy binary was resolved (CaddyResolver returned null).
    Disabled = 3,

    // Proxy app is explicitly not running (operator stopped it via UI / API).
    Stopped = 4
}
