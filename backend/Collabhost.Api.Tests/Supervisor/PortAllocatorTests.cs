using System.Net;
using System.Net.Sockets;

using Collabhost.Api.Supervisor;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

public class PortAllocatorTests
{
    [Fact]
    public async Task AllocateAsync_ReturnsAPortInTheValidRange()
    {
        var allocator = new PortAllocator();

        var port = await allocator.AllocateAsync(CancellationToken.None);

        port.ShouldBeGreaterThan(0);
        port.ShouldBeLessThanOrEqualTo(65535);
    }

    [Fact]
    public void Reserve_MarksThePortReserved()
    {
        var allocator = new PortAllocator();

        allocator.Reserve(Ulid.NewUlid(), 41000);

        allocator.IsReserved(41000).ShouldBeTrue();
    }

    [Fact]
    public void Release_ReturnsThePortToThePool()
    {
        var allocator = new PortAllocator();
        var appId = Ulid.NewUlid();

        allocator.Reserve(appId, 41000);
        allocator.Release(appId);

        allocator.IsReserved(41000).ShouldBeFalse();
    }

    [Fact]
    public void Reserve_IsIdempotentForTheSameAppAndPort()
    {
        var allocator = new PortAllocator();
        var appId = Ulid.NewUlid();

        allocator.Reserve(appId, 41000);
        allocator.Reserve(appId, 41000);

        allocator.IsReserved(41000).ShouldBeTrue();
    }

    [Fact]
    public void Reserve_RePinningSameAppToNewPort_ReleasesThePriorReservation()
    {
        var allocator = new PortAllocator();
        var appId = Ulid.NewUlid();

        allocator.Reserve(appId, 41000);
        allocator.Reserve(appId, 42000);

        allocator.IsReserved(41000).ShouldBeFalse();
        allocator.IsReserved(42000).ShouldBeTrue();
    }

    [Fact]
    public void Release_ForADifferentApp_DoesNotReleaseAnothersReservation()
    {
        var allocator = new PortAllocator();
        var pinned = Ulid.NewUlid();
        var other = Ulid.NewUlid();

        allocator.Reserve(pinned, 41000);
        allocator.Release(other);

        allocator.IsReserved(41000).ShouldBeTrue();
    }

    [Fact]
    public async Task AllocateAsync_NeverReturnsAReservedPort()
    {
        var allocator = new PortAllocator();

        // Reserve a wide band so that, across many automatic allocations, the
        // reservation gate is exercised rather than merely missed by chance.
        for (var port = 40000; port <= 40100; port++)
        {
            allocator.Reserve(Ulid.NewUlid(), port);
        }

        for (var i = 0; i < 200; i++)
        {
            var allocated = await allocator.AllocateAsync(CancellationToken.None);

            allocator.IsReserved(allocated).ShouldBeFalse
            (
                "Automatic allocation returned a reserved port."
            );
        }
    }

    [Fact]
    public async Task AllocateAsync_HonorsCancellation()
    {
        var allocator = new PortAllocator();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>
        (
            async () => await allocator.AllocateAsync(cts.Token)
        );
    }

    [Fact]
    public async Task IsPortAvailable_ReturnsTrueForAFreePort()
    {
        // Allocate a port, then immediately release it so nothing is bound -- the
        // probe should report it free.
        var allocator = new PortAllocator();
        var freePort = await allocator.AllocateAsync(CancellationToken.None);

        PortAllocator.IsPortAvailable(freePort).ShouldBeTrue();
    }

    [Fact]
    public void IsPortAvailable_ReturnsFalseWhenSomethingElseHoldsThePort()
    {
        // Bind a loopback listener to simulate an owner OUTSIDE the allocator's
        // reservation registry (another host service, a container, a leftover) --
        // exactly the case a reservation cannot see. The probe must report it taken.
        using var occupier = new TcpListener(IPAddress.Loopback, 0);
        occupier.Start();
        var heldPort = ((IPEndPoint)occupier.LocalEndpoint).Port;

        PortAllocator.IsPortAvailable(heldPort).ShouldBeFalse();

        occupier.Stop();
    }

    [Fact]
    public void AllocateInfrastructurePort_NeverReturnsAReservedPort()
    {
        // The proxy admin port is drawn through this path. It must honor the same
        // reservation registry as managed-app allocation so a pinned port can never
        // be handed to the admin API.
        var allocator = new PortAllocator();

        for (var port = 40000; port <= 40100; port++)
        {
            allocator.Reserve(Ulid.NewUlid(), port);
        }

        for (var i = 0; i < 200; i++)
        {
            var allocated = allocator.AllocateInfrastructurePort();

            allocator.IsReserved(allocated).ShouldBeFalse
            (
                "Infrastructure allocation returned a reserved port."
            );
        }
    }
}
