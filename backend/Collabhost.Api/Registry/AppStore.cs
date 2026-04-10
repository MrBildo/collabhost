using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive -- cache key interpolation is safe
public class AppStore
(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache,
    ILogger<AppStore> logger
)
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IMemoryCache _cache = cache
        ?? throw new ArgumentNullException(nameof(cache));

    private readonly ILogger<AppStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public async Task<App?> GetBySlugAsync(string slug, CancellationToken ct) =>
        await _cache.GetOrCreateAsync($"app:slug:{slug}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Apps
                .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Slug == slug, ct);
        });

    public async Task<App?> GetByIdAsync(Ulid id, CancellationToken ct) =>
        await _cache.GetOrCreateAsync($"app:id:{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Apps
                .AsNoTracking()
                    .SingleOrDefaultAsync(a => a.Id == id, ct);
        });

    public async Task<IReadOnlyList<App>> ListAsync(CancellationToken ct) =>
        await _cache.GetOrCreateAsync("apps:list", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Apps
                .OrderBy(a => a.Slug)
                .AsNoTracking()
                    .ToListAsync(ct);
        }) ?? [];

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.Apps
                .AnyAsync(a => a.Slug == slug, ct);
    }

    public async Task<IReadOnlyDictionary<string, CapabilityOverride>> GetOverridesAsync
    (
        Ulid appId,
        CancellationToken ct
    ) =>
        await _cache.GetOrCreateAsync($"overrides:{appId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.CapabilityOverrides
                .Where(o => o.AppId == appId)
                .AsNoTracking()
                    .ToDictionaryAsync(o => o.CapabilitySlug, StringComparer.Ordinal, ct);
        }) ?? new Dictionary<string, CapabilityOverride>(StringComparer.Ordinal);

    public async Task UpdateAppAsync(App app, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        db.Apps.Attach(app);
        db.Entry(app).Property(a => a.DisplayName).IsModified = true;
        db.Entry(app).Property(a => a.ModifiedAt).IsModified = true;

        await db.SaveChangesAsync(ct);

        InvalidateAppCache(app.Slug);
        _cache.Remove($"app:id:{app.Id}");
    }

    public async Task<App> CreateAsync(App app, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        db.Apps.Add(app);
        await db.SaveChangesAsync(ct);

        InvalidateAppCache(app.Slug);
        _cache.Remove($"app:id:{app.Id}");

        _logger.LogInformation("Created app {Slug}", app.Slug);

        return app;
    }

    public async Task SaveOverrideAsync
    (
        Ulid appId,
        string capabilitySlug,
        string configurationJson,
        CancellationToken ct
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.CapabilityOverrides
                .SingleOrDefaultAsync
                (
                    o => o.AppId == appId && o.CapabilitySlug == capabilitySlug,
                    ct
                );

        if (existing is not null)
        {
            existing.ConfigurationJson = configurationJson;
        }
        else
        {
            db.CapabilityOverrides.Add(new CapabilityOverride
            {
                AppId = appId,
                CapabilitySlug = capabilitySlug,
                ConfigurationJson = configurationJson
            });
        }

        await db.SaveChangesAsync(ct);

        InvalidateOverrides(appId);
    }

    public async Task DeleteAppAsync(Ulid appId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var app = await db.Apps
                .SingleOrDefaultAsync(a => a.Id == appId, ct);

        if (app is null)
        {
            return;
        }

        var overrides = await db.CapabilityOverrides
            .Where(o => o.AppId == appId)
                .ToListAsync(ct);

        db.CapabilityOverrides.RemoveRange(overrides);
        db.Apps.Remove(app);

        await db.SaveChangesAsync(ct);

        InvalidateAppCache(app.Slug);
        _cache.Remove($"app:id:{appId}");
        InvalidateOverrides(appId);

        _logger.LogInformation("Deleted app {Slug}", app.Slug);
    }

    public void Invalidate(string slug) => InvalidateAppCache(slug);

    public void InvalidateOverrides(Ulid appId) =>
        _cache.Remove($"overrides:{appId}");

    private void InvalidateAppCache(string slug)
    {
        _cache.Remove($"app:slug:{slug}");
        _cache.Remove("apps:list");
    }
}
#pragma warning restore MA0076
