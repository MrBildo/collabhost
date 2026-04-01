namespace Collabhost.Api.Features.Filesystem;

public class FilesystemModule : IFeatureModule
{
    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/filesystem")
            .WithTags("Filesystem");

        group.MapGet("/browse", Browse.HandleAsync);

        return endpoints;
    }
}
