namespace Collabhost.Api.Supervisor;

// Lets an infrastructure subsystem (today: the proxy admin API) claim a port
// through the same PortAllocator registry that pinned apps reserve into -- but
// only once every pinned reservation has been hydrated from app config.
//
// The supervisor invokes each registered initializer exactly once, after
// HydratePinnedPortReservationsAsync and BEFORE the auto-start loop. That window
// is the only point where (a) the allocator already knows every pinned port and
// (b) nothing has been started yet, so an infrastructure port the proxy needs
// before the proxy process spawns can be allocated without colliding with a pin
// and without being handed a number a pin will later claim. This is what makes
// the "a reserved port is never handed out" guarantee absolute rather than scoped
// to the app-start path. (#373 completeness contract, item 2.)
public interface IReservedPortInitializer
{
    void Initialize(PortAllocator allocator);
}
