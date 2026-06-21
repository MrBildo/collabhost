using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Operations;

namespace Collabhost.Api.Proxy;

// Reload the proxy configuration (code-structure-conventions §8/§9 -- a concrete operation in its
// owning subsystem; the first spine op outside Registry/). The trivial, app-less operation: no
// app lookup, no branch. The body is intent only -- it requests a config regeneration, records the
// actor-stamped event, and returns the empty success outcome. There is no exception-to-result
// handling in the leaf: the base hoists the InvalidOperationException-to-Conflict mapping, though
// RequestSync only enqueues a channel write and does not throw. There is no hand-built
// ActivityEvent either: the base's app-less RecordAsync stamps the actor. And there is no
// surface-result construction -- each surface maps ProxyReloadOutcome to its own shape (REST 204
// No Content / 409 Conflict, MCP a fixed "reload requested" message / InvalidParameters).
//
// The one precondition: while the proxy is disabled (the post-launch probe gave up and route sync
// is suppressed), a reload is a no-op -- RequestSync would enqueue a sync the disabled latch drops.
// Returning the empty Success then would falsely report 204 No Content. So the operation returns a
// Conflict carrying an operator-actionable message instead, surfacing the disabled state rather
// than silently swallowing the request.
public sealed class ReloadProxyOperation
(
    ProxyManager proxy,
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : Operation<ReloadProxyCommand, ProxyReloadOutcome>(currentUser, activityEventStore)
{
    private readonly ProxyManager _proxy = proxy
        ?? throw new ArgumentNullException(nameof(proxy));

    protected override async Task<OperationResult<ProxyReloadOutcome>> ExecuteCoreAsync
    (
        ReloadProxyCommand command,
        CancellationToken ct
    )
    {
        if (_proxy.IsProxyDisabled)
        {
            return OperationResult<ProxyReloadOutcome>.Conflict
            (
                "Proxy is disabled -- the admin API did not become ready and route sync is "
                + "suspended. A reload has no effect until the proxy recovers. Check the proxy "
                + "process and proxy logs."
            );
        }

        _proxy.RequestSync();

        await RecordAsync(ActivityEventTypes.ProxyReloaded, ct);

        return OperationResult<ProxyReloadOutcome>.Success(new ProxyReloadOutcome());
    }
}
