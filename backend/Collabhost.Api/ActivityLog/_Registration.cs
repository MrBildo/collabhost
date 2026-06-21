namespace Collabhost.Api.ActivityLog;

public static class ActivityLogRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddActivityLog(IConfiguration configuration)
        {
            services.AddSingleton<ActivityEventStore>();

            // SVC-01: bound the insert-only ActivityEvents table on a timer. Resolve the retention
            // policy from the Diagnostics: namespace (alongside crash-log retention) and run the
            // sweep as a hosted service off the insert hot path.
            services.AddSingleton(ActivityEventRetentionSettings.Resolve(configuration));
            services.AddHostedService<ActivityEventRetentionService>();

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
