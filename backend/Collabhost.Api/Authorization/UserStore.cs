using System.Globalization;
using Collabhost.Api.Data;

namespace Collabhost.Api.Authorization;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive -- cache key interpolation is safe
public class UserStore
(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache,
    ILogger<UserStore> logger
)
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly IMemoryCache _cache = cache
        ?? throw new ArgumentNullException(nameof(cache));

    private readonly ILogger<UserStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

    public async Task<User?> GetByAuthKeyAsync(string authKey, CancellationToken ct)
    {
        if (_cache.TryGetValue($"user:key:{authKey}", out User? cached))
        {
            return cached;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
            .AsNoTracking()
                .SingleOrDefaultAsync(u => u.AuthKey == authKey, ct);

        if (user is not null)
        {
            _cache.Set($"user:key:{authKey}", user, _cacheDuration);
            _cache.Set($"user:id:{user.Id}", user, _cacheDuration);
        }

        return user;
    }

    public async Task<User?> GetByIdAsync(Ulid id, CancellationToken ct) =>
        await _cache.GetOrCreateAsync($"user:id:{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Users
                .AsNoTracking()
                    .SingleOrDefaultAsync(u => u.Id == id, ct);
        });

    public async Task<IReadOnlyList<User>> GetAllAsync(CancellationToken ct) =>
        await _cache.GetOrCreateAsync("users:list", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            return await db.Users
                .OrderBy(u => u.CreatedAt)
                .AsNoTracking()
                    .ToListAsync(ct);
        }) ?? [];

    public async Task<User> CreateAsync
    (
        string name,
        UserRole role,
        CancellationToken ct
    )
    {
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Name = name,
            AuthKey = Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture),
            Role = role
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        _cache.Remove("users:list");

        _logger.LogInformation("Created user {Name} with role {Role}", user.Name, user.Role);

        return user;
    }

    public async Task DeactivateAsync(Ulid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users
                .SingleOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return;
        }

        if (user.Role == UserRole.Administrator)
        {
            var activeAdminCount = await db.Users
                .Where(u => u.Role == UserRole.Administrator && u.IsActive)
                    .CountAsync(ct);

            if (activeAdminCount <= 1)
            {
                throw new InvalidOperationException("Cannot deactivate the last active administrator");
            }
        }

        user.IsActive = false;
        await db.SaveChangesAsync(ct);

        _cache.Remove($"user:id:{id}");
        _cache.Remove($"user:key:{user.AuthKey}");
        _cache.Remove("users:list");

        _logger.LogInformation("Deactivated user {Name} ({Id})", user.Name, id);
    }

    public async Task<bool> AnyExistAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.Users.AnyAsync(ct);
    }
}
#pragma warning restore MA0076
