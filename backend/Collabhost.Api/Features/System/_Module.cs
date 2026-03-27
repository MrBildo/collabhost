namespace Collabhost.Api.Features.System;

public class SystemModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1")
            .WithTags("System");

        group.MapGet("/status", GetStatus.Handle);

        return endpoints;
    }
}
