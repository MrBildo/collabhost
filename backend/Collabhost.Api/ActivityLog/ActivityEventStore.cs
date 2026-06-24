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

        if (query.Cursor is not null && Ulid.TryParse(query.Cursor, CultureInfo.InvariantCulture, out var cursor))
        {
            // Keyset pagination: take the page of events strictly older than the cursor. ULIDs are
            // lexicographically time-ordered, so a relational comparison on the Id IS temporal order.
            // The Id column carries a Ulid<->string value converter; string.CompareTo / string.Compare
            // do NOT translate through the converter on EF Core 10 / SQLite (the converter defeats
            // relational translation -- dotnet/efcore#35515), but Ulid.CompareTo on the converted CLR
            // property does: EF emits `WHERE Id < @cursor` against the stored string column. Malformed
            // cursors fail the TryParse and are ignored (the request returns the first page) rather
            // than throwing -- the cursor is untrusted client input. (#432.)
            q = q.Where(e => e.Id.CompareTo(cursor) < 0);
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

    // SVC-01 retention sweep. Bounds the insert-only table on two axes -- delete a row that violates
    // EITHER MaxAge (older than the window) OR MaxCount (beyond the newest N). Each axis is a single
    // set-based ExecuteDeleteAsync (no entity materialization). Returns the total rows removed.
    // Best-effort: a transient failure is logged and the next sweep retries; retention is hygiene,
    // not correctness, so it must never take down the host.
    public async Task<int> PruneAsync(ActivityEventRetentionSettings retention, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(retention);

        var removed = 0;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            if (retention.MaxAgeDays > 0)
            {
                var cutoff = DateTime.UtcNow.AddDays(-retention.MaxAgeDays);

                removed += await db.ActivityEvents
                    .Where(e => e.Timestamp < cutoff)
                        .ExecuteDeleteAsync(ct);
            }

            if (retention.MaxCount > 0)
            {
                // Keep only the newest MaxCount rows; delete everything else. Id is a ULID --
                // lexicographically time-ordered, so OrderByDescending(e => e.Id).Take(MaxCount) is
                // the keep-set, and the delete removes every row NOT in it. The keep-set is a
                // correlated subquery EF translates to a single SQL `DELETE ... WHERE Id NOT IN
                // (SELECT Id ... ORDER BY Id DESC LIMIT N)` -- no row materialization, and (unlike a
                // string-CompareTo boundary, which SQLite cannot translate) it works on the converted
                // Ulid column directly.
                var keepSet = db.ActivityEvents
                    .OrderByDescending(e => e.Id)
                        .Take(retention.MaxCount)
                            .Select(e => e.Id);

                removed += await db.ActivityEvents
                    .Where(e => !keepSet.Contains(e.Id))
                        .ExecuteDeleteAsync(ct);
            }

            if (removed > 0)
            {
                _logger.LogInformation
                (
                    "Activity-event retention sweep removed {Removed} row(s)",
                    removed
                );
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Activity-event retention sweep failed");
        }

        return removed;
    }

    public static string DeriveSeverity(string eventType) => eventType switch
    {
        ActivityEventTypes.AppCrashed => "error",
        ActivityEventTypes.AppFatal => "error",
        ActivityEventTypes.AppKilled => "warning",
        _ => "info"
    };
}
