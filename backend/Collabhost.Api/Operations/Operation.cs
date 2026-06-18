using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Operations;

// The operation base (code-structure-conventions §8). It hoists -- once -- the two plumbing
// strands every mutating leaf repeats by hand across both surfaces:
//
//   1. try/catch -> Conflict. The supervisor's start/stop/restart/kill throw
//      InvalidOperationException on a bad state transition, and every surface maps that to
//      409/InvalidParameters today (the catch block is repeated 8x across the current
//      handlers). The base catches it and returns OperationResult.Conflict; the leaf body
//      stays intent-only and lets the supervisor exception bubble. The leaf returns explicit
//      NotFound/Validation/Conflict for its OWN checks (slug-not-found, validation).
//
//   2. The actor-stamped event recorder. RecordAsync stamps the acting user (id + name) onto
//      an ActivityEvent and persists it via ActivityEventStore, so the leaf writes
//      RecordAsync(ActivityEventTypes.AppStarted, app, ct) instead of hand-building the
//      six-field event with currentUser.UserId.ToString() / currentUser.User.Name every time
//      (~15 lines, repeated ~12x). The recorder lives on the base (base-ctor deps), not as an
//      injected collaborator: stamping the actor IS the base's job, a separate collaborator
//      whose only consumer is the base would be the pool-by-kind ceremony §9 forbids, and
//      base-ctor deps keep the leaf ctor shorter (it passes the two deps to base(...) and
//      never names them again).
//
// What the base deliberately does NOT do (faithful to §8): no SaveChanges (the store is the
// persistence boundary -- ActivityEventStore.RecordAsync and AppStore own SaveChanges, the
// base only calls them); no dispatcher / mediator / assembly-scan (surfaces inject the concrete
// operation and call ExecuteAsync directly); no auth (REST auth is middleware, MCP auth is the
// per-call McpRequestAuthenticator at the surface -- the operation runs auth-agnostic).
public abstract class Operation<TCommand, TResult>
(
    ICurrentUser currentUser,
    ActivityEventStore activityEventStore
) : IOperation<TCommand, TResult>
{
    private readonly ICurrentUser _currentUser = currentUser
        ?? throw new ArgumentNullException(nameof(currentUser));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    public async Task<OperationResult<TResult>> ExecuteAsync(TCommand command, CancellationToken ct)
    {
        try
        {
            return await ExecuteCoreAsync(command, ct);
        }
        catch (InvalidOperationException exception)
        {
            return OperationResult<TResult>.Conflict(exception.Message);
        }
    }

    // The leaf overrides exactly this method; its body is intent only -- load, act, record,
    // shape the outcome. No try/catch, no ActivityEvent-by-hand, no surface-result construction.
    protected abstract Task<OperationResult<TResult>> ExecuteCoreAsync(TCommand command, CancellationToken ct);

    // Record an activity event against a live App entity, stamped with the acting user. The
    // common case: the operation is acting on an app it just loaded, so id + slug come from
    // the entity.
    protected Task RecordAsync(string eventType, App app, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(app);

        return RecordAsync
        (
            eventType,
            app.Id.ToString(null, CultureInfo.InvariantCulture),
            app.Slug,
            metadataJson: null,
            ct
        );
    }

    // Record an activity event against an app's id + slug directly, stamped with the acting
    // user. For the case the live entity is gone by the time the event is recorded (delete,
    // which removes the row before emitting app.deleted) or carries metadata.
    protected Task RecordAsync
    (
        string eventType,
        string appId,
        string appSlug,
        string? metadataJson,
        CancellationToken ct
    )
    {
        var activityEvent = new ActivityEvent
        {
            EventType = eventType,
            ActorId = _currentUser.UserId.ToString(null, CultureInfo.InvariantCulture),
            ActorName = _currentUser.User.Name,
            AppId = appId,
            AppSlug = appSlug,
            MetadataJson = metadataJson
        };

        return _activityEventStore.RecordAsync(activityEvent, ct);
    }
}
