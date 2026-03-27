using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Domain;

public static class AppTypeBehavior
{
    // Process model
    public static bool HasProcess(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.StaticSite;

    public static bool SupportsEnvVars(Guid appTypeId) =>
        HasProcess(appTypeId);

    public static bool SupportsHealthCheck(Guid appTypeId) =>
        HasProcess(appTypeId) && IsRoutable(appTypeId);

    // Routing
    public static bool IsRoutable(Guid appTypeId) =>
        appTypeId != IdentifierCatalog.AppTypes.ProxyService;

    public static string ProxyMode(Guid appTypeId) => appTypeId switch
    {
        _ when appTypeId == IdentifierCatalog.AppTypes.StaticSite => "file_server",
        _ when IsRoutable(appTypeId) => "reverse_proxy",
        _ => "none"
    };

    // Protection
    public static bool IsProtected(Guid appTypeId) =>
        appTypeId == IdentifierCatalog.AppTypes.ProxyService;

    public static bool IsDeletable(Guid appTypeId) =>
        !IsProtected(appTypeId);

    // Startup
    public static int StartupPriority(Guid appTypeId) =>
        IsProtected(appTypeId) ? 0 : 1;
}
