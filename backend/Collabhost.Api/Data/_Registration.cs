using Microsoft.Data.Sqlite;

namespace Collabhost.Api.Data;

public static class DataRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDataAccess(IConfiguration configuration)
        {
            var (connectionString, dataDir) = ResolveConnectionString(configuration);

            // Ensure the data directory exists on first boot (§12.2)
            if (!string.IsNullOrEmpty(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));

            services.AddSingleton<MigrationRunner>();

            return services;
        }
    }

    // Internal visibility for unit tests
    internal static (string ConnectionString, string? DataDir) ResolveConnectionString(IConfiguration configuration)
    {
        // COLLABHOST_DATA_PATH: env var wins over appsettings, then hardcoded default (§12.3 precedence)
        var dataPath = Environment.GetEnvironmentVariable("COLLABHOST_DATA_PATH");

        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            var dbFile = Path.Combine(dataPath, "collabhost.db");

            return ($"Data Source={dbFile}", dataPath);
        }

        var configuredConnectionString = configuration.GetConnectionString("Host")
            ?? "Data Source=./data/collabhost.db";

        var configuredDataDir = new SqliteConnectionStringBuilder(configuredConnectionString).DataSource is { } src
            ? Path.GetDirectoryName(src)
            : null;

        return (configuredConnectionString, configuredDataDir);
    }
}
