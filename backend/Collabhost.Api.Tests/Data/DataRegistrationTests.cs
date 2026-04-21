using Collabhost.Api.Data;

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
    public void ResolveConnectionString_EnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var (connectionString, _) = DataRegistration.ResolveConnectionString(EmptyConfig());

        connectionString.ShouldBe("Data Source=./data/collabhost.db");
    }

    [Fact]
    public void ResolveConnectionString_EnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", "   ");

        try
        {
            var config = ConfigWith("ConnectionStrings:Host", "Data Source=./data/collabhost.db");

            var (connectionString, _) = DataRegistration.ResolveConnectionString(config);

            connectionString.ShouldBe("Data Source=./data/collabhost.db");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);
        }
    }

    [Fact]
    public void ResolveConnectionString_ConfigWithAdditionalParams_ExtractsDataDirCleanly()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);

        var config = ConfigWith
        (
            "ConnectionStrings:Host",
            "Data Source=./data/collabhost.db;Cache=Shared;Mode=ReadWriteCreate"
        );

        var (connectionString, dataDir) = DataRegistration.ResolveConnectionString(config);

        connectionString.ShouldBe("Data Source=./data/collabhost.db;Cache=Shared;Mode=ReadWriteCreate");
        // Path.GetDirectoryName is platform-sensitive: use it on both sides for a stable cross-platform assertion
        dataDir.ShouldBe(Path.GetDirectoryName("./data/collabhost.db"));
    }
}
