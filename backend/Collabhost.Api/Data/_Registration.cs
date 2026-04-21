namespace Collabhost.Api.Data;

public static class DataRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDataAccess(IConfiguration configuration)
        {
            var connectionString = ResolveConnectionString(configuration);

            // Ensure the data directory exists on first boot (§12.2)
            var dbPath = connectionString.Replace("Data Source=", "", StringComparison.Ordinal);
            var dataDir = Path.GetDirectoryName(dbPath);

            if (!string.IsNullOrEmpty(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

            return services;
        }
    }

    // Internal visibility for unit tests
    internal static string ResolveConnectionString(IConfiguration configuration)
    {
        // COLLABHOST_DATA_PATH: env var wins over appsettings, then hardcoded default (§12.3 precedence)
        var dataPath = Environment.GetEnvironmentVariable("COLLABHOST_DATA_PATH");

        return !string.IsNullOrWhiteSpace(dataPath)
            ? $"Data Source={Path.Combine(dataPath, "collabhost.db")}"
            : configuration.GetConnectionString("Host") ?? "Data Source=./data/collabhost.db";
    }
}
