namespace Collabhost.Api.Data;

public static class DataRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDataAccess(IConfiguration configuration)
        {
            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseSqlite(
                    configuration.GetConnectionString("Host")
                    ?? "Data Source=./db/collabhost.db"));

            return services;
        }
    }
}
