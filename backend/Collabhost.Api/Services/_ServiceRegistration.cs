using Collabhost.Api.Data.Interceptors;

using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCollabhostDatabase(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("Host")
                ?? "Data Source=./db/collabhost.db";

            var databasePath = connectionString.Replace("Data Source=", "", StringComparison.Ordinal);
            var databaseDirectory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (databaseDirectory is not null)
            {
                Directory.CreateDirectory(databaseDirectory);
            }

            services.AddDbContext<CollabhostDbContext>
            (
                options => options
                    .UseSqlite(connectionString)
                    .AddInterceptors(new AuditInterceptor())
            );

            return services;
        }

        public IServiceCollection AddInfrastructureServices()
        {
            services.AddScoped<PortAllocator>();
            services.AddSingleton<IManagedProcessRunner, WindowsProcessRunner>();
            services.AddSingleton<ProcessSupervisor>();
            services.AddHostedService<ProcessSupervisor>(sp => sp.GetRequiredService<ProcessSupervisor>());
            services.AddSingleton<UpdateCoordinator>();

            return services;
        }

        public IServiceCollection AddProxyServices(IConfiguration configuration)
        {
            services.Configure<ProxySettings>(configuration.GetSection("Proxy"));
            services.AddSingleton(sp => sp.GetRequiredService<IOptions<ProxySettings>>().Value);
            services.AddHttpClient<CaddyProxyConfigClient>();
            services.AddSingleton<IProxyConfigClient>(sp => sp.GetRequiredService<CaddyProxyConfigClient>());
            services.AddSingleton<ProxyConfigGenerator>();
            services.AddSingleton<ProxyConfigManager>();
            services.AddHostedService(sp => sp.GetRequiredService<ProxyConfigManager>());

            return services;
        }
    }
}
