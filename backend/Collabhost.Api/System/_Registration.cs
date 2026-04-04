namespace Collabhost.Api.Platform;

public static class SystemRegistration
{
    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapSystemEndpoints()
        {
            SystemEndpoints.Map(routes);
            return routes;
        }
    }
}
