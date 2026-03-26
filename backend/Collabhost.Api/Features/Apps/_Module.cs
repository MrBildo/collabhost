using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public class AppsModule : IFeatureModule
{
    public IServiceCollection RegisterServices(IServiceCollection services)
    {
        services.AddScoped<Create.Handler>();
        services.AddScoped<Get.Handler>();
        services.AddScoped<GetAll.Handler>();
        services.AddScoped<Update.Handler>();
        services.AddScoped<Delete.Handler>();
        services.AddScoped<PortAllocator>();

        return services;
    }

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
