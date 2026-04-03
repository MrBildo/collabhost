namespace Collabhost.Api.Dashboard;

public static class DashboardRegistration
{
    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapDashboardEndpoints()
        {
            DashboardEndpoints.Map(routes);
            return routes;
        }
    }
}
