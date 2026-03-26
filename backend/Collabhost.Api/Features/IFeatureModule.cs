namespace Collabhost.Api.Features;

public interface IFeatureModule
{
    IServiceCollection RegisterServices(IServiceCollection services);
    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints);
}
