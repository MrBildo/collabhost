namespace Collabhost.Api.Data;

public static class DataRegistration
{
    public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("Host")
                ?? "Data Source=./db/collabhost.db"));

        return services;
    }
}
