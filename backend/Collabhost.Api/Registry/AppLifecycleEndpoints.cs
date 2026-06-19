using Collabhost.Api.Operations;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
internal static class AppLifecycleEndpoints
{
    // All four lifecycle actions are migrated to the operation spine (code-structure-conventions
    // §8): the endpoint is a thin adapter -- inject the concrete operation directly (no dispatcher),
    // adapt the route slug into the command, call it, and map OperationResult<AppActionOutcome> back
    // to exactly the AppActionResult / Problem the handler returned before. Start and stop are
    // dual-branch (routing-only vs process) inside StartAppOperation / StopAppOperation; the adapter
    // shape here is identical to restart/kill because the branching lives in the operation.
    internal static async Task<IResult> StartAppAsync
    (
        string slug,
        StartAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new StartAppCommand(slug), ct);

        return result.ToHttpResult();
    }

    internal static async Task<IResult> StopAppAsync
    (
        string slug,
        StopAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new StopAppCommand(slug), ct);

        return result.ToHttpResult();
    }

    internal static async Task<IResult> RestartAppAsync
    (
        string slug,
        RestartAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new RestartAppCommand(slug), ct);

        return result.ToHttpResult();
    }

    internal static async Task<IResult> KillAppAsync
    (
        string slug,
        KillAppOperation operation,
        CancellationToken ct
    )
    {
        var result = await operation.ExecuteAsync(new KillAppCommand(slug), ct);

        return result.ToHttpResult();
    }
}

// File-scoped mapping from the surface-agnostic operation outcome back to the REST result shape
// (§7: the surface holds only its file-scoped mapping, never the contract types). This is the
// REST half of the outcome-mapping template PRs 3-7 copy. K-1 (Kai's PR-1 forward note):
// OperationResult.FailureKind defaults to ordinal-0 NotFound on a success, so the success arm is
// gated on IsSuccess FIRST -- FailureKind is only read on the failure path. The three failure
// kinds map to the exact statuses the pre-migration handlers returned: NotFound -> 404 (empty
// body, as TypedResults.NotFound() did), Validation -> 400, Conflict -> 409 (the supervisor's
// InvalidOperationException, formerly the catch block, now hoisted to the Operation<,> base).
file static class AppLifecycleResultMapping
{
    public static IResult ToHttpResult(this OperationResult<AppActionOutcome> result)
    {
        if (result.IsSuccess)
        {
            var outcome = result.Value!;
            var actions = AppEndpoints.BuildActions(outcome.HasProcess, outcome.HasRouting, outcome.State);

            return TypedResults.Ok
            (
                new AppActionResult(outcome.Id.ToString(), outcome.State.ToApiString(), actions)
            );
        }

        return result.FailureKind switch
        {
            OperationFailureKind.NotFound => TypedResults.NotFound(),
            OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
            _ => TypedResults.Problem(result.Error, statusCode: 409),
        };
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
