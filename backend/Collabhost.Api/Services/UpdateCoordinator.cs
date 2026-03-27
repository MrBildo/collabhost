using System.Collections.Concurrent;

namespace Collabhost.Api.Services;

public class UpdateCoordinator
{
    private readonly ConcurrentDictionary<Guid, byte> _active = new();

    public bool TryAcquire(Guid appId) => _active.TryAdd(appId, 0);

    public void Release(Guid appId) => _active.TryRemove(appId, out _);
}
