using Collabhost.Api.Supervisor;

namespace Collabhost.Api.Proxy;

// Allocates the Caddy admin-API port through the shared PortAllocator so it is
// drawn from the same pool pinned apps reserve into -- and reserves it back, so
// no later dynamic allocation for a managed app can be handed the admin port
// either. Replaces the old DI-registration-time static PortAllocator.AllocatePort()
// call, which ran before any reservation existed and was blind to pinned ports.
//
// Runs as an IReservedPortInitializer: the supervisor invokes it after every pin
// is hydrated and before the auto-start loop, which is the only correct window --
// the proxy process is auto-started in that loop and reads its admin port at start,
// so the port must be assigned first, and it must be assigned with the pins already
// known so the exclusion is real. (#373 completeness contract, item 2.)
public sealed class ProxyAdminPortInitializer
(
    ProxySettings settings,
    ILogger<ProxyAdminPortInitializer> logger
) : IReservedPortInitializer
{
    // ULID identity for the admin port's reservation. The proxy admin port is not
    // a managed app, so it has no App.Id; the empty ULID is a sentinel that cannot
    // collide with any real app's id and keeps the reservation idempotent across
    // re-initialization within a process lifetime.
    private static readonly Ulid _adminPortReservationId = Ulid.Empty;

    private readonly ProxySettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ILogger<ProxyAdminPortInitializer> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public void Initialize(PortAllocator allocator)
    {
        ArgumentNullException.ThrowIfNull(allocator);

        var adminPort = allocator.AllocateInfrastructurePort();

        allocator.Reserve(_adminPortReservationId, adminPort);
        _settings.AdminPort = adminPort;

        _logger.LogInformation
        (
            "Allocated and reserved proxy admin port {AdminPort}",
            adminPort
        );
    }
}
