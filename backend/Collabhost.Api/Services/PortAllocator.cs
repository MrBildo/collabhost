using System.Net;
using System.Net.Sockets;

namespace Collabhost.Api.Services;

public class PortAllocator(CollabhostDbContext db)
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<int> AllocateAsync(CancellationToken ct = default)
    {
        const int maxAttempts = 10;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var port = FindFreePort();
            var isInUse = await _db.Apps.AnyAsync(a => a.Port == port, ct);

            if (!isInUse)
            {
                return port;
            }
        }

        throw new InvalidOperationException("Unable to allocate a free port after multiple attempts.");
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
