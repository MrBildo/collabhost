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

    // The four "anchored to ContentRoot" tests below intentionally use AppContext.BaseDirectory
    // as the content root. Behaviorally, this asserts the env-var-unset posture (Aspire dev,
    // Windows installer, Linux user-scope) where ContentRootPath equals AppContext.BaseDirectory
    // -- exactly the path #246 (c2-A) preserves. The "EnvVarSet_AnchorsRelativeToContentRoot"
    // test below exercises the other posture: a custom ContentRoot drives the resolution.

    [Fact]
    public void ResolveConnectionString_EnvVarSet_WinsOverConfig()
    {
        var dataPath = Path.GetTempPath();

        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", dataPath);

        try
        {
            var config = ConfigWith("ConnectionStrings:Host", "Data Source=./other/collabhost.db");

            var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config, AppContext.BaseDirectory);

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

        var (connectionString, _) = DataRegistration.ResolveConnectionString(config, AppContext.BaseDirectory);

        connectionString.ShouldBe("Data Source=/custom/path/collabhost.db");
    }

    [Fact]
    public void ResolveConnectionString_EnvVarUnsetConfigUnset_AnchorsHardcodedDefaultToContentRoot()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(EmptyConfig(), AppContext.BaseDirectory);

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

            var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config, AppContext.BaseDirectory);

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

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config, AppContext.BaseDirectory);

        var expectedDbFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "./data/collabhost.db"));

        connectionString.ShouldContain($"Data Source={expectedDbFile}");
        connectionString.ShouldContain("Cache=Shared");
        connectionString.ShouldContain("Mode=ReadWriteCreate");
        dataDir.ShouldBe(Path.GetDirectoryName(expectedDbFile));
    }

    [Fact]
    public void ResolveConnectionString_CustomContentRoot_AnchorsRelativeDataSourceToContentRoot()
    {
        // Card #247: when ASPNETCORE_CONTENTROOT diverges from AppContext.BaseDirectory
        // (system-install layout), the relative connection string must anchor to the host's
        // ContentRootPath, NOT to the binary directory.
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var customContentRoot = Path.Combine(Path.GetTempPath(), $"collabhost-cr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(customContentRoot);

        try
        {
            var config = ConfigWith("ConnectionStrings:Host", "Data Source=./data/collabhost.db");

            var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config, customContentRoot);

            var expectedDbFile = Path.GetFullPath(Path.Combine(customContentRoot, "./data/collabhost.db"));

            connectionString.ShouldBe($"Data Source={expectedDbFile}");
            dataDir.ShouldBe(Path.GetDirectoryName(expectedDbFile));
        }
        finally
        {
            Directory.Delete(customContentRoot, recursive: true);
        }
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

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config, AppContext.BaseDirectory);

        connectionString.ShouldBe($"Data Source={absoluteDb}");
        dataDir.ShouldBe(Path.GetDirectoryName(absoluteDb));
    }

    [Fact]
    public void ResolveConnectionString_NullContentRoot_Throws() =>
        Should.Throw<ArgumentException>(() => DataRegistration.ResolveConnectionString(EmptyConfig(), null!));

    [Fact]
    public void ResolveConnectionString_WhitespaceContentRoot_Throws() =>
        Should.Throw<ArgumentException>(() => DataRegistration.ResolveConnectionString(EmptyConfig(), "  "));
}
