using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Services;

public interface IProcessStateNameResolver
{
    Task<string> ResolveDisplayNameAsync(Guid stateId, CancellationToken ct = default);
}

internal sealed class ProcessStateNameResolver(CollabhostDbContext db) : IProcessStateNameResolver
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    private Dictionary<Guid, string>? _cache;

    public async Task<string> ResolveDisplayNameAsync(Guid stateId, CancellationToken ct = default)
    {
        _cache ??= await LoadProcessStatesAsync(ct);

        return _cache.TryGetValue(stateId, out var displayName)
            ? displayName
            : "Unknown";
    }

    private async Task<Dictionary<Guid, string>> LoadProcessStatesAsync(CancellationToken ct) =>
        await _db.Set<ProcessState>()
            .AsNoTracking()
            .ToDictionaryAsync(ps => ps.Id, ps => ps.DisplayName, ct);
}
