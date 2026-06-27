using Collabhost.Api.Operations;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Registry;

// Migrated to the operation spine (code-structure-conventions §8): the endpoint is a thin adapter --
// validate the slug shape (a REST-surface concern: REST takes request.Name AS the slug, so it
// validates it as-given and returns the raw Slug.Validate error; this is the single-surface transform
// that stays at the surface), assemble request.Values into the normalized CreateAppCommand (the
// REST-specific "discovery" virtual section folds into the "process" capability -- the genuine
// REST<->MCP divergence is this input assembly, not the shared core), inject the concrete operation
// directly (no dispatcher), call it, and map the OperationResult back to exactly the result the
// handler returned before. The shared exists -> type -> validate -> create -> save -> route -> events
// -> hints sequence now lives once in CreateAppOperation.
//
// REST owns the SUCCESS result shaping: 201 Created with CreateAppResponse (id + the per-app writable
// data path + the hints the operation computed). The validation of each section + the
// ResolveHelpfulNextSteps hint computation moved INTO the operation (validation per Marcus §1.3, the
// hint dedup per §2.1), so neither lives at this surface anymore.
internal static class AppRegistrationEndpoints
{
    internal static async Task<IResult> CreateAppAsync
    (
        CreateAppRequest request,
        CreateAppOperation operation,
        AppDataPathResolver dataPathResolver,
        CancellationToken ct
    )
    {
        // Slug-shape validation stays at the REST surface (Marcus §1.5): REST takes request.Name as
        // the slug and returns the raw Slug.Validate error verbatim with 400. The command carries the
        // FINAL PERSISTED slug form -- request.Name.Trim().ToLowerInvariant() -- so the operation never
        // re-transforms (Marcus R7).
        var (isValid, error) = Slug.Validate(request.Name);

        if (!isValid)
        {
            return TypedResults.Problem(error, statusCode: 400);
        }

        var slug = request.Name.Trim().ToLowerInvariant();

        var overrides = request.AssembleOverrides();

        var command = new CreateAppCommand(slug, request.DisplayName, request.AppTypeSlug, overrides);

        var result = await operation.ExecuteAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        var outcome = result.Value!;

        return TypedResults.Created
        (
            $"/api/v1/apps/{outcome.Slug}",
            new CreateAppResponse
            (
                outcome.Id.ToCanonicalString(),
                dataPathResolver.ResolveFor(outcome.Slug),
                outcome.Hints
            )
        );
    }
}

// File-scoped adapters between the REST surface and the operation spine (§7: the surface holds only
// its file-scoped mapping). The request adapter assembles CreateAppRequest's typed nested dictionary
// into the normalized JsonObject the operation validates -- folding the REST-only "discovery" virtual
// section into the "process" capability (the discovery section is a registration-only concept; its
// fields merge into process, and the merged process object is validated like any other section by the
// operation). Each JsonElement -> JsonNode by raw text, byte-identical to the pre-migration per-field
// JsonNode.Parse(fieldValue.GetRawText()). The result mapping is the FAILURE half (success is shaped
// inline in CreateAppAsync as 201 Created).
file static class CreateAppOperationAdapter
{
    public static JsonObject AssembleOverrides(this CreateAppRequest request)
    {
        var overrides = new JsonObject();

        if (request.Values is null)
        {
            return overrides;
        }

        // Collect process overrides from both the real "process" section and the "discovery" virtual
        // section -- the discovery section is a registration-only concept that maps onto the process
        // capability. The operation validates the merged process object once, like any other section.
        JsonObject? processOverrides = null;

        foreach (var (sectionKey, sectionValues) in request.Values)
        {
            if (string.Equals(sectionKey, "discovery", StringComparison.Ordinal))
            {
                processOverrides ??= [];

                foreach (var (fieldKey, fieldValue) in sectionValues)
                {
                    processOverrides[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
                }

                continue;
            }

            var sectionObject = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionValues)
            {
                sectionObject[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            if (string.Equals(sectionKey, "process", StringComparison.Ordinal))
            {
                processOverrides ??= [];

                foreach (var property in sectionObject)
                {
                    processOverrides[property.Key] = property.Value?.DeepClone();
                }

                continue;
            }

            overrides[sectionKey] = sectionObject;
        }

        if (processOverrides is not null)
        {
            overrides["process"] = processOverrides;
        }

        return overrides;
    }

    // K-1 (Kai's PR-1 forward note): OperationResult.FailureKind defaults to ordinal-0 NotFound on a
    // success, so the caller gates on IsSuccess BEFORE reaching this mapping -- it is the failure half
    // only. The three kinds map to the exact statuses the pre-migration handler returned: NotFound ->
    // 404 (the "App type not found." message), Validation -> 400 (the bare section-qualified joined
    // errors, verbatim, OR the slug error -- though slug-invalid is short-circuited at the surface
    // above this mapping), Conflict -> 409 (the "An app with slug '...' already exists." message).
    public static IResult ToHttpResult(this OperationResult<CreateAppOutcome> result) =>
        result.FailureKind switch
        {
            OperationFailureKind.NotFound => TypedResults.Problem(result.Error, statusCode: 404),
            OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
            _ => TypedResults.Problem(result.Error, statusCode: 409),
        };
}
