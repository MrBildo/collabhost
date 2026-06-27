namespace Collabhost.Api.Authorization;

public enum UserRole
{
    Administrator,
    Agent,
    // Read-only observability tier: below Agent. Appended last on purpose -- the role is
    // persisted by its integer value (Users.Role is an INTEGER column), so the ordinal is
    // storage identity and must never be reordered; existing Administrator (0) / Agent (1)
    // rows would silently remap. Privilege level is NOT the ordinal -- it is decided in
    // RequireRoleFilter and Entitlements, where ReadOnly cannot mutate the control plane and
    // is denied reads that can surface secrets (logs, settings) or operational history (events).
    ReadOnly,
}
