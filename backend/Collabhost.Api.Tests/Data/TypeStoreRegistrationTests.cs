using Collabhost.Api.Data.AppTypes;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class TypeStoreRegistrationTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value })
            .Build();

    [Fact]
    public void ResolveSettings_EnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", "/opt/collabhost/user-types");

        try
        {
            var config = ConfigWith("TypeStore:UserTypesDirectory", "/other/user-types");

            var result = TypeStoreRegistration.ResolveSettings(config);

            result.UserTypesDirectory.ShouldBe("/opt/collabhost/user-types");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);
        }
    }

    [Fact]
    public void ResolveSettings_EnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        var config = ConfigWith("TypeStore:UserTypesDirectory", "/custom/user-types");

        var result = TypeStoreRegistration.ResolveSettings(config);

        result.UserTypesDirectory.ShouldBe("/custom/user-types");
    }

    [Fact]
    public void ResolveSettings_EnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        var result = TypeStoreRegistration.ResolveSettings(EmptyConfig());

        result.UserTypesDirectory.ShouldBe("UserTypes");
    }

    [Fact]
    public void ResolveSettings_EnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", "  ");

        try
        {
            var config = ConfigWith("TypeStore:UserTypesDirectory", "/fallback/user-types");

            var result = TypeStoreRegistration.ResolveSettings(config);

            result.UserTypesDirectory.ShouldBe("/fallback/user-types");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);
        }
    }
}
