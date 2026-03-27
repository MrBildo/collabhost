using Collabhost.Api.Domain;
using Collabhost.Api.Domain.Catalogs;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests;

public class AppTypeBehaviorTests
{
    private static Guid ResolveAppTypeId(string label) => label switch
    {
        "Executable" => IdentifierCatalog.AppTypes.Executable,
        "NpmPackage" => IdentifierCatalog.AppTypes.NpmPackage,
        "StaticSite" => IdentifierCatalog.AppTypes.StaticSite,
        "ProxyService" => IdentifierCatalog.AppTypes.ProxyService,
        _ => throw new ArgumentException($"Unknown app type label: {label}")
    };

    [Theory]
    [InlineData("Executable", true, true, true, true, false, true, "reverse_proxy", 1)]
    [InlineData("NpmPackage", true, true, true, true, false, true, "reverse_proxy", 1)]
    [InlineData("StaticSite", false, false, false, true, false, true, "file_server", 1)]
    [InlineData("ProxyService", true, true, false, false, true, false, "none", 0)]
    public void BehaviorMatrix_ReturnsExpectedValues
    (
        string label,
        bool hasProcess,
        bool supportsEnvVars,
        bool supportsHealthCheck,
        bool isRoutable,
        bool isProtected,
        bool isDeletable,
        string proxyMode,
        int startupPriority
    )
    {
        // Arrange
        var appTypeId = ResolveAppTypeId(label);

        // Act & Assert
        AppTypeBehavior.HasProcess(appTypeId).ShouldBe(hasProcess, $"{label}.HasProcess");
        AppTypeBehavior.SupportsEnvVars(appTypeId).ShouldBe(supportsEnvVars, $"{label}.SupportsEnvVars");
        AppTypeBehavior.SupportsHealthCheck(appTypeId).ShouldBe(supportsHealthCheck, $"{label}.SupportsHealthCheck");
        AppTypeBehavior.IsRoutable(appTypeId).ShouldBe(isRoutable, $"{label}.IsRoutable");
        AppTypeBehavior.IsProtected(appTypeId).ShouldBe(isProtected, $"{label}.IsProtected");
        AppTypeBehavior.IsDeletable(appTypeId).ShouldBe(isDeletable, $"{label}.IsDeletable");
        AppTypeBehavior.ProxyMode(appTypeId).ShouldBe(proxyMode, $"{label}.ProxyMode");
        AppTypeBehavior.StartupPriority(appTypeId).ShouldBe(startupPriority, $"{label}.StartupPriority");
    }
}
