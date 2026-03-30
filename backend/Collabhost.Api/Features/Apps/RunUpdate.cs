namespace Collabhost.Api.Features.Apps;

public static class RunUpdate
{
    public static async Task HandleAsync
    (
        string externalId,
        CollabhostDbContext db,
        HttpContext context,
        CancellationToken ct
    )
    {
        var app = await db.FindAppByExternalIdAsync(externalId, ct);

        if (app is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "NOT_FOUND", message = "App not found." }, ct);
            return;
        }

        // UpdateCommand is removed in the capability model — this endpoint is a stub until Card #39
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = "NOT_SUPPORTED", message = "App updates are not supported in the capability model yet." }, ct);
    }
}
