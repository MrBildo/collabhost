namespace Collabhost.Api.Features.Lookups;

public sealed class LookupsModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/lookups")
            .WithTags("Lookups");

        group.MapGet("/app-types", GetAppTypes.HandleAsync);

        return endpoints;
    }
}
