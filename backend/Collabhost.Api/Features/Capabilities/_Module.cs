namespace Collabhost.Api.Features.Capabilities;

public sealed class CapabilitiesModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/capabilities")
            .WithTags("Capabilities");

        group.MapGet("/", GetAll.HandleAsync);

        return endpoints;
    }
}
