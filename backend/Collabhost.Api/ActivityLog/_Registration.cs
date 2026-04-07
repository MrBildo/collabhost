namespace Collabhost.Api.ActivityLog;

public static class ActivityLogRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddActivityLog()
        {
            services.AddSingleton<ActivityEventStore>();
            return services;
        }
    }

    extension(IEndpointRouteBuilder routes)
    {
        public IEndpointRouteBuilder MapActivityLogEndpoints()
        {
            ActivityLogEndpoints.Map(routes);
            return routes;
        }
    }
}
