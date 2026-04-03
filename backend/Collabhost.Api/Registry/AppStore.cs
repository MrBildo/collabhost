using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;

namespace Collabhost.Api.Registry;

public sealed class AppStore
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

    // ---- Reads ----

    public async Task<App?> GetBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Apps
            .Include(a => a.AppType)
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Slug == slug, ct);
    }

    public async Task<App?> GetByIdAsync(Ulid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Apps
            .Include(a => a.AppType)
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == id, ct);
    }

    public async Task<IReadOnlyList<App>> ListAsync(CancellationToken ct) =>
        await _cache.GetOrCreateAsync("apps:list", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            return await db.Apps
                .Include(a => a.AppType)
                .OrderBy(a => a.Slug)
                .AsNoTracking()
                .ToListAsync(ct);
        }) ?? [];

    public async Task<bool> ExistsBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Apps.AnyAsync(a => a.Slug == slug, ct);
    }

    public async Task<AppType?> GetAppTypeBySlugAsync(string slug, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AppTypes
            .Include(t => t.Bindings)
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Slug == slug, ct);
    }

    public async Task<IReadOnlyList<AppType>> ListAppTypesAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AppTypes
            .OrderBy(t => t.DisplayName)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    // ---- Capability data loading (for resolver) ----

    public async Task<IReadOnlyList<CapabilityBinding>> GetBindingsAsync(Ulid appTypeId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CapabilityBindings
            .Where(b => b.AppTypeId == appTypeId)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, CapabilityOverride>> GetOverridesAsync
    (
        Ulid appId,
        CancellationToken ct
    )
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CapabilityOverrides
            .Where(o => o.AppId == appId)
            .AsNoTracking()
            .ToDictionaryAsync(o => o.CapabilitySlug, StringComparer.Ordinal, ct);
    }

    // ---- Writes (invalidate cache) ----

    public async Task<App> CreateAsync(App app, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.Apps.Add(app);
        await db.SaveChangesAsync(ct);
        InvalidateAppCache(app.Slug);
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
            .SingleOrDefaultAsync(
                o => o.AppId == appId && o.CapabilitySlug == capabilitySlug, ct);

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
    }

    public async Task DeleteAppAsync(Ulid appId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var app = await db.Apps.SingleOrDefaultAsync(a => a.Id == appId, ct);
        if (app is null)
        {
            return;
        }

        // Remove overrides
        var overrides = await db.CapabilityOverrides
            .Where(o => o.AppId == appId)
            .ToListAsync(ct);
        db.CapabilityOverrides.RemoveRange(overrides);

        db.Apps.Remove(app);
        await db.SaveChangesAsync(ct);
        InvalidateAppCache(app.Slug);
        _logger.LogInformation("Deleted app {Slug}", app.Slug);
    }

    // ---- Cache invalidation ----

    public void Invalidate(string slug) => InvalidateAppCache(slug);

    private void InvalidateAppCache(string slug)
    {
        _cache.Remove($"app:{slug}");
        _cache.Remove("apps:list");
    }
}
