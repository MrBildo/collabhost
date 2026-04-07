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

    [Fact]
    public void CanAccessEndpoint_Administrator_UsersPrefix_ReturnsTrue()
    {
        Entitlements.CanAccessEndpoint(UserRole.Administrator, "/api/v1/users").ShouldBeTrue();
        Entitlements.CanAccessEndpoint(UserRole.Administrator, "/api/v1/users/01ABCD").ShouldBeTrue();
    }

    [Fact]
    public void CanAccessEndpoint_Agent_UsersPrefix_ReturnsFalse()
    {
        Entitlements.CanAccessEndpoint(UserRole.Agent, "/api/v1/users").ShouldBeFalse();
        Entitlements.CanAccessEndpoint(UserRole.Agent, "/api/v1/users/01ABCD").ShouldBeFalse();
    }

    [Fact]
    public void CanAccessEndpoint_Agent_NonAdminPaths_ReturnsTrue()
    {
        Entitlements.CanAccessEndpoint(UserRole.Agent, "/api/v1/apps").ShouldBeTrue();
        Entitlements.CanAccessEndpoint(UserRole.Agent, "/api/v1/app-types").ShouldBeTrue();
        Entitlements.CanAccessEndpoint(UserRole.Agent, "/api/v1/auth/me").ShouldBeTrue();
    }
}
