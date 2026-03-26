namespace Collabhost.Api.Features.Apps;

public class AppsModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/apps")
            .WithTags("Apps");

        group.MapPost("/", Create.HandleAsync);
        group.MapGet("/", GetAll.HandleAsync);
        group.MapGet("/{externalId}", Get.HandleAsync);
        group.MapPut("/{externalId}", Update.HandleAsync);
        group.MapDelete("/{externalId}", Delete.HandleAsync);

        return endpoints;
    }
}
