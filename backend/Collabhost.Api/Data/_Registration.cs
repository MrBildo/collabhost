using Microsoft.Data.Sqlite;

namespace Collabhost.Api.Data;

public static class DataRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddDataAccess(IConfiguration configuration)
        {
            var (connectionString, dataDir) = ResolveConnectionString(configuration);

            // Ensure the data directory exists on first boot
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
        // COLLABHOST_DATA_PATH: env var wins over appsettings, then hardcoded default
        var dataPath = Environment.GetEnvironmentVariable("COLLABHOST_DATA_PATH");

        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            var dbFile = Path.Combine(dataPath, "collabhost.db");

            return ($"Data Source={dbFile}", dataPath);
        }

        var configuredConnectionString = configuration.GetConnectionString("Host")
            ?? "Data Source=./data/collabhost.db";

        // Anchor a relative DataSource to AppContext.BaseDirectory (the binary dir). SQLite opens
        // relative paths against the process CWD, and the host's content root no longer follows
        // CWD after card #164 -- without this anchor, the installed shipped connection string
        // `./data/collabhost.db` would still land in whatever directory the operator launched
        // from, and the dataDir returned here (used by preflight + migrations) would disagree
        // with where SQLite actually put the file.
        var normalizedConnectionString = NormalizeRelativeDataSource(configuredConnectionString);

        var configuredDataDir = new SqliteConnectionStringBuilder(normalizedConnectionString).DataSource is { } src
            ? Path.GetDirectoryName(src)
            : null;

        return (normalizedConnectionString, configuredDataDir);
    }

    private static string NormalizeRelativeDataSource(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrEmpty(builder.DataSource) || Path.IsPathRooted(builder.DataSource))
        {
            return connectionString;
        }

        builder.DataSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, builder.DataSource));

        return builder.ConnectionString;
    }
}
