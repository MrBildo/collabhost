using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Collabhost.Api.Supervisor;

// Hands out free loopback ports for managed apps and keeps a registry of
// pinned ports that must never be handed out automatically.
//
// Automatic allocation asks the kernel for any free port at the instant of
// the call (bind to port 0). That is correct for apps nothing addresses
// directly, but an app pinned to a fixed port needs that port held back from
// the automatic pool -- otherwise the kernel could hand the same number to a
// different app while the pinned app is stopped, and the pinned app would fail
// to bind on restart. The reservation registry is what makes a pin durable.
//
// Registered as a singleton so every start goes through the same registry.
public class PortAllocator
{
    // Per-app reservations, keyed by the app's identity so a reservation can be
    // updated (re-pin to a different port) or released (app deleted) cleanly.
    private readonly ConcurrentDictionary<Ulid, int> _reservationsByApp = new();

    public Task<int> AllocateAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(AllocateFreePort());
    }

    public static int AllocatePort() => FindFreePort();

    // Reserve a fixed port for an app, holding it back from automatic
    // allocation. Idempotent for the same app+port; re-pinning an app to a
    // different port replaces its prior reservation.
    public void Reserve(Ulid appId, int port) => _reservationsByApp[appId] = port;

    // Release an app's reservation -- e.g. when the app is deleted -- so the
    // port returns to the automatic-allocation pool.
    public void Release(Ulid appId) => _reservationsByApp.TryRemove(appId, out _);

    public bool IsReserved(int port) => _reservationsByApp.Values.Contains(port);

    // Find a free port the kernel offers that is not currently reserved for a
    // pinned app. The kernel can legitimately offer a reserved port number when
    // the pinned app is stopped (nothing is bound to it at that instant), so the
    // reservation set is the gate, not the bind itself. Re-rolls a bounded
    // number of times before giving up rather than looping forever against a
    // pathologically narrow free range.
    private int AllocateFreePort()
    {
        const int maxAttempts = 50;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = FindFreePort();

            if (!IsReserved(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException
        (
            "Unable to allocate a free port that does not collide with a reserved "
            + $"(pinned) port after {maxAttempts} attempts."
        );
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
