using Collabhost.Api.Data;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class DataRegistrationTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value })
            .Build();

    [Fact]
    public void ResolveConnectionString_EnvVarSet_WinsOverConfig()
    {
        var dataPath = Path.GetTempPath();

        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", dataPath);

        try
        {
            var config = ConfigWith("ConnectionStrings:Host", "Data Source=./other/collabhost.db");

            var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config);

            connectionString.ShouldBe($"Data Source={Path.Combine(dataPath, "collabhost.db")}");
            dataDir.ShouldBe(dataPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);
        }
    }

    [Fact]
    public void ResolveConnectionString_EnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var config = ConfigWith("ConnectionStrings:Host", "Data Source=/custom/path/collabhost.db");

        var (connectionString, _) = DataRegistration.ResolveConnectionString(config);

        connectionString.ShouldBe("Data Source=/custom/path/collabhost.db");
    }

    [Fact]
    public void ResolveConnectionString_EnvVarUnsetConfigUnset_AnchorsHardcodedDefaultToBaseDirectory()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(EmptyConfig());

        var expectedDbFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "./data/collabhost.db"));

        connectionString.ShouldBe($"Data Source={expectedDbFile}");
        dataDir.ShouldBe(Path.GetDirectoryName(expectedDbFile));
    }

    [Fact]
    public void ResolveConnectionString_EnvVarWhitespace_FallsBackToConfigAndAnchorsRelative()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", "   ");

        try
        {
            var config = ConfigWith("ConnectionStrings:Host", "Data Source=./data/collabhost.db");

            var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config);

            var expectedDbFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "./data/collabhost.db"));

            connectionString.ShouldBe($"Data Source={expectedDbFile}");
            dataDir.ShouldBe(Path.GetDirectoryName(expectedDbFile));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);
        }
    }

    [Fact]
    public void ResolveConnectionString_ConfigWithAdditionalParams_AnchorsDataSourceAndPreservesParams()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var config = ConfigWith
        (
            "ConnectionStrings:Host",
            "Data Source=./data/collabhost.db;Cache=Shared;Mode=ReadWriteCreate"
        );

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config);

        var expectedDbFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "./data/collabhost.db"));

        connectionString.ShouldContain($"Data Source={expectedDbFile}");
        connectionString.ShouldContain("Cache=Shared");
        connectionString.ShouldContain("Mode=ReadWriteCreate");
        dataDir.ShouldBe(Path.GetDirectoryName(expectedDbFile));
    }

    [Fact]
    public void EnableWalJournalMode_OnFreshDatabase_PersistsWalInHeader()
    {
        // Card #205: PRAGMA journal_mode=WAL must fire and SQLite must accept it.
        // SQLite persists journal_mode in the DB file header on first write, so opening
        // a separate connection afterwards should still report `wal`.
        var dbFile = Path.Combine(Path.GetTempPath(), $"collabhost-wal-test-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbFile}";

        try
        {
            DataRegistration.EnableWalJournalMode(connectionString);

            using var verify = new SqliteConnection(connectionString);
            verify.Open();

            using var command = verify.CreateCommand();
            command.CommandText = "PRAGMA journal_mode;";

            var mode = (string?)command.ExecuteScalar();

            mode.ShouldNotBeNull();
            mode.ShouldBe("wal", StringCompareShould.IgnoreCase);
        }
        finally
        {
            // Pool clears so the file handle releases on Windows before we delete.
            SqliteConnection.ClearAllPools();

            foreach (var path in new[] { dbFile, $"{dbFile}-wal", $"{dbFile}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void ResolveConnectionString_ConfigWithAbsolutePath_IsLeftUnchanged()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var absoluteDb = Path.Combine(Path.GetTempPath(), "custom-collabhost.db");
        var config = ConfigWith("ConnectionStrings:Host", $"Data Source={absoluteDb}");

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config);

        connectionString.ShouldBe($"Data Source={absoluteDb}");
        dataDir.ShouldBe(Path.GetDirectoryName(absoluteDb));
    }
}
