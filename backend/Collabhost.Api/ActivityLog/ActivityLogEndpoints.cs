using System.Globalization;

namespace Collabhost.Api.ActivityLog;

public record ActivityEventItem
(
    string Id,
    string EventType,
    string ActorId,
    string ActorName,
    string? AppId,
    string? AppSlug,
    JsonElement? Metadata,
    DateTime Timestamp,
    string Severity
);

public record ActivityEventListResponse
(
    IReadOnlyList<ActivityEventItem> Items,
    string? NextCursor,
    bool HasMore
);

public static class ActivityLogEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/v1/events")
            .WithTags("ActivityLog");

        group.MapGet("/", QueryEventsAsync);
    }

    private static async Task<IResult> QueryEventsAsync
    (
        string? category,
        string? appSlug,
        string? actorId,
        string? eventType,
        DateTime? since,
        DateTime? until,
        int? limit,
        string? cursor,
        ActivityEventStore store,
        CancellationToken ct
    )
    {
        var query = new ActivityEventQuery
        (
            Category: category,
            AppSlug: appSlug,
            ActorId: actorId,
            EventType: eventType,
            Since: since,
            Until: until,
            Limit: Math.Min(limit ?? 50, 200),
            Cursor: cursor
        );

        var page = await store.QueryAsync(query, ct);

        var items = page.Items
            .Select(e => MapToItem(e))
                .ToArray();

        return TypedResults.Ok(new ActivityEventListResponse(items, page.NextCursor, page.HasMore));
    }

    private static ActivityEventItem MapToItem(ActivityEvent e)
    {
        JsonElement? metadata = null;

        if (e.MetadataJson is not null)
        {
            metadata = JsonDocument.Parse(e.MetadataJson).RootElement;
        }

        return new ActivityEventItem
        (
            Id: e.Id.ToString(null, CultureInfo.InvariantCulture),
            EventType: e.EventType,
            ActorId: e.ActorId,
            ActorName: e.ActorName,
            AppId: e.AppId,
            AppSlug: e.AppSlug,
            Metadata: metadata,
            Timestamp: e.Timestamp,
            Severity: ActivityEventStore.DeriveSeverity(e.EventType)
        );
    }
}
