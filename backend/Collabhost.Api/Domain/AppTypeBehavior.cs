using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Domain;

public static class AppTypeBehavior
{
    // Process model
    public static bool HasProcess(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.StaticSite;

    public static bool SupportsEnvironmentVariables(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.StaticSite;

    public static bool SupportsHealthCheck(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.StaticSite
        && appTypeId != IdentifierCatalog.AppTypes.ProxyService;

    // Routing
    public static bool IsRoutable(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.ProxyService;

    public static string ProxyMode(Guid appTypeId) => appTypeId switch
    {
        _ when appTypeId == IdentifierCatalog.AppTypes.StaticSite => "file_server",
        _ when appTypeId == IdentifierCatalog.AppTypes.ProxyService => "none",
        _ => "reverse_proxy"
    };

    // Protection
    public static bool IsProtected(Guid appTypeId) =>
        appTypeId == IdentifierCatalog.AppTypes.ProxyService;

    public static bool IsDeletable(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.ProxyService;

    // Startup
    public static int StartupPriority(Guid appTypeId) =>
        appTypeId == IdentifierCatalog.AppTypes.ProxyService ? 0 : 1;
}
