using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Collabhost.Api.HealthChecks;

public static class HealthCheckRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHealthCheckExecutor()
        {
            services.TryAddSingleton(TimeProvider.System);

            // Health probes hit localhost endpoints managed by Collabhost itself. The
            // probe's per-call CancellationTokenSource enforces the configured timeout.
            // Wrapping this client with the standard resilience pipeline (retries,
            // circuit breakers) would conflict with the executor's own intervalSeconds
            // rhythm and produce confusing "still healthy" results when an app is
            // actually thrashing. Constructing a private HttpClient bypasses
            // ConfigureHttpClientDefaults entirely -- same reasoning as ICaddyClient
            // (see Proxy/_Registration.cs).
            services.AddSingleton<HealthCheckProbe>(provider =>
                new HealthCheckProbe
                (
                    new HttpClient(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<HealthCheckProbe>>()
                ));

            services.AddSingleton<HealthCheckExecutorService>();
            services.AddSingleton<IHealthCheckExecutor>(provider =>
                provider.GetRequiredService<HealthCheckExecutorService>());

            services.AddHostedService(provider =>
                provider.GetRequiredService<HealthCheckExecutorService>());

            return services;
        }
    }
}
