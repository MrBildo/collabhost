namespace Collabhost.Api.Features.Proxy;

public class ProxyModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var routeGroup = endpoints
            .MapGroup("/api/v1/routes")
            .WithTags("Routes");

        routeGroup.MapGet("/", GetRoutes.HandleAsync);

        var proxyGroup = endpoints
            .MapGroup("/api/v1/proxy")
            .WithTags("Proxy");

        proxyGroup.MapPost("/reload", Reload.HandleAsync);
        proxyGroup.MapGet("/status", GetProxyStatus.HandleAsync);

        return endpoints;
    }
}
