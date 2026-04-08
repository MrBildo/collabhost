using System.Globalization;

using Collabhost.Api.Data;

namespace Collabhost.Api.ActivityLog;

// Registered as singleton so it can be injected into both singleton hosted services
// (ProcessSupervisor, ProxyManager) and scoped MCP tools. Uses IDbContextFactory to
// create short-lived contexts per operation -- no captive dependency issues.
public class ActivityEventStore
(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ActivityEventStore> logger
)
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly ILogger<ActivityEventStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task RecordAsync(ActivityEvent activityEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(activityEvent);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.ActivityEvents.Add(activityEvent);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event of type '{EventType}'", activityEvent.EventType);
        }
    }

    public async Task<IReadOnlyList<ActivityEvent>> GetRecentAsync(int limit, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.ActivityEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
                .Take(limit)
                    .ToListAsync(ct);
    }

    public async Task<ActivityEventPage> QueryAsync(ActivityEventQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var q = db.ActivityEvents
            .AsNoTracking()
            .AsQueryable();

        if (query.Category is not null)
        {
            var prefix = query.Category + ".";
            q = q.Where(e => e.EventType.StartsWith(prefix));
        }

        if (query.AppSlug is not null)
        {
            q = q.Where(e => e.AppSlug == query.AppSlug);
        }

        if (query.ActorId is not null)
        {
            q = q.Where(e => e.ActorId == query.ActorId);
        }

        if (query.EventType is not null)
        {
            q = q.Where(e => e.EventType == query.EventType);
        }

        if (query.Since is not null)
        {
            q = q.Where(e => e.Timestamp >= query.Since.Value);
        }

        if (query.Until is not null)
        {
            q = q.Where(e => e.Timestamp <= query.Until.Value);
        }

        if (query.Cursor is not null)
        {
            // Keyset pagination: compare the raw stored string column (varchar 26) directly.
            // Crockford Base32 ULIDs are lexicographically ordered by time, so SQLite string
            // comparison on the stored column is equivalent to temporal ordering.
            var cursor = query.Cursor;
            q = q.Where(e => EF.Property<string>(e, "Id").CompareTo(cursor) < 0);
        }

        var pageSize = Math.Min(query.Limit, 200);

        var items = await q
            .OrderByDescending(e => e.Id)
                .Take(pageSize + 1)
                    .ToListAsync(ct);

        var hasMore = items.Count > pageSize;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var nextCursor = hasMore ? items[^1].Id.ToString(null, CultureInfo.InvariantCulture) : null;

        return new ActivityEventPage(items, nextCursor, hasMore);
    }

    public static string DeriveSeverity(string eventType) => eventType switch
    {
        ActivityEventTypes.AppCrashed => "error",
        ActivityEventTypes.AppFatal => "error",
        ActivityEventTypes.AppKilled => "warning",
        _ => "info"
    };
}
