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

    [Fact]
    public void ResolveEffectiveUserTypesDirectory_AbsolutePath_ReturnedAsIs()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        var absolute = OperatingSystem.IsWindows() ? @"C:\custom\user-types" : "/custom/user-types";
        var settings = TypeStoreRegistration.ResolveSettings(ConfigWith("TypeStore:UserTypesDirectory", absolute));

        var result = TypeStoreRegistration.ResolveEffectiveUserTypesDirectory(settings, AppContext.BaseDirectory);

        result.ShouldBe(absolute);
    }

    [Fact]
    public void ResolveEffectiveUserTypesDirectory_RelativePath_ComposedAgainstContentRoot()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        var settings = TypeStoreRegistration.ResolveSettings(EmptyConfig());

        // env-var-unset posture: ContentRootPath equals AppContext.BaseDirectory.
        var result = TypeStoreRegistration.ResolveEffectiveUserTypesDirectory(settings, AppContext.BaseDirectory);

        result.ShouldBe(Path.Combine(AppContext.BaseDirectory, "UserTypes"));
    }

    [Fact]
    public void ResolveEffectiveUserTypesDirectory_RelativePath_ComposedAgainstCustomContentRoot()
    {
        // Card #247: when ASPNETCORE_CONTENTROOT diverges from AppContext.BaseDirectory
        // (system-install layout), relative user-types directories must anchor to the
        // host's ContentRootPath, NOT to the binary directory.
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);

        var customContentRoot = OperatingSystem.IsWindows() ? @"C:\etc\collabhost" : "/etc/collabhost";
        var settings = TypeStoreRegistration.ResolveSettings(EmptyConfig());

        var result = TypeStoreRegistration.ResolveEffectiveUserTypesDirectory(settings, customContentRoot);

        result.ShouldBe(Path.Combine(customContentRoot, "UserTypes"));
    }

    [Fact]
    public void ResolveEffectiveUserTypesDirectory_NullContentRoot_Throws()
    {
        var settings = TypeStoreRegistration.ResolveSettings(EmptyConfig());

        Should.Throw<ArgumentException>(() => TypeStoreRegistration.ResolveEffectiveUserTypesDirectory(settings, null!));
    }
}
