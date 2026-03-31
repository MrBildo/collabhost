namespace Collabhost.Api.Features.AppTypes;

public sealed class AppTypesModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/app-types")
            .WithTags("AppTypes");

        group.MapGet("/", GetAll.HandleAsync);
        group.MapGet("/{externalId}", Get.HandleAsync);
        group.MapPost("/", Create.HandleAsync);
        group.MapPut("/{externalId}", Update.HandleAsync);
        group.MapDelete("/{externalId}", Delete.HandleAsync);

        return endpoints;
    }
}
