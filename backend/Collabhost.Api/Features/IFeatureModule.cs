namespace Collabhost.Api.Features;

public interface IFeatureModule
{
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints);
}
