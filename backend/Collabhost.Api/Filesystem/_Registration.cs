namespace Collabhost.Api.Filesystem;

public static class FilesystemRegistration
{
    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapFilesystemEndpoints()
        {
            FilesystemEndpoints.Map(routes);
            return routes;
        }
    }
}
