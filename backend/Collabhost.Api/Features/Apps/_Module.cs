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

        group.MapPost("/{externalId}/start", Start.HandleAsync);
        group.MapPost("/{externalId}/stop", Stop.HandleAsync);
        group.MapPost("/{externalId}/restart", Restart.HandleAsync);
        group.MapPost("/{externalId}/kill", Kill.HandleAsync);
        group.MapGet("/{externalId}/status", GetStatus.HandleAsync);
        group.MapGet("/{externalId}/logs", GetLogs.HandleAsync);

        return endpoints;
    }
}
