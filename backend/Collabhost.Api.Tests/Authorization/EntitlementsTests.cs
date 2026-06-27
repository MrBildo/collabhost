using Collabhost.Api.Authorization;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

public class EntitlementsTests
{
    [Fact]
    public void CanAccessTool_Administrator_ReturnsTrue_ForAllTools()
    {
        Entitlements.CanAccessTool(UserRole.Administrator, "delete_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Administrator, "list_apps").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Administrator, "update_settings").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Administrator, "kill_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Administrator, "register_app").ShouldBeTrue();
    }

    [Fact]
    public void CanAccessTool_Agent_DeleteApp_ReturnsFalse() =>
        Entitlements.CanAccessTool(UserRole.Agent, "delete_app").ShouldBeFalse();

    [Fact]
    public void CanAccessTool_Agent_UpdateSettings_ReturnsTrue() =>
        Entitlements.CanAccessTool(UserRole.Agent, "update_settings").ShouldBeTrue();

    [Fact]
    public void CanAccessTool_Agent_KillApp_ReturnsTrue() =>
        Entitlements.CanAccessTool(UserRole.Agent, "kill_app").ShouldBeTrue();

    [Fact]
    public void CanAccessTool_Agent_ListApps_ReturnsTrue() =>
        Entitlements.CanAccessTool(UserRole.Agent, "list_apps").ShouldBeTrue();

    [Fact]
    public void CanAccessTool_Agent_ListEvents_ReturnsTrue() =>
        Entitlements.CanAccessTool(UserRole.Agent, "list_events").ShouldBeTrue();

    [Fact]
    public void CanAccessTool_Agent_AllReadOnlyTools_ReturnsTrue()
    {
        Entitlements.CanAccessTool(UserRole.Agent, "get_system_status").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "get_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "list_app_types").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "get_logs").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "get_settings").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "list_routes").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "browse_filesystem").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "detect_strategy").ShouldBeTrue();
    }

    [Fact]
    public void CanAccessTool_Agent_AllMutationTools_ReturnsTrue()
    {
        Entitlements.CanAccessTool(UserRole.Agent, "start_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "stop_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "restart_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "reload_proxy").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.Agent, "register_app").ShouldBeTrue();
    }

    // Property: on the MCP surface the read-only tier is granted the non-secret observability
    // reads it exists for -- status, app list/detail, type catalog, route listing.
    [Fact]
    public void CanAccessTool_ReadOnly_NonSecretReads_ReturnsTrue()
    {
        Entitlements.CanAccessTool(UserRole.ReadOnly, "get_system_status").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "list_apps").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "get_app").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "list_app_types").ShouldBeTrue();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "list_routes").ShouldBeTrue();
    }

    // Property: the read-only tier is denied reads that can surface secrets or operational
    // history -- logs, settings, activity events. This is the real delta over the Agent role,
    // which is granted all three.
    [Fact]
    public void CanAccessTool_ReadOnly_SecretBearingReads_ReturnsFalse()
    {
        Entitlements.CanAccessTool(UserRole.ReadOnly, "get_logs").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "get_settings").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "list_events").ShouldBeFalse();
    }

    // Property: the read-only tier cannot mutate the control plane on the MCP surface.
    [Fact]
    public void CanAccessTool_ReadOnly_AllMutationTools_ReturnsFalse()
    {
        Entitlements.CanAccessTool(UserRole.ReadOnly, "start_app").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "stop_app").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "restart_app").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "kill_app").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "update_settings").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "register_app").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "reload_proxy").ShouldBeFalse();
        Entitlements.CanAccessTool(UserRole.ReadOnly, "delete_app").ShouldBeFalse();
    }
}
