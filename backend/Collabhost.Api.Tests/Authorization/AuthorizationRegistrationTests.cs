using Collabhost.Api.Authorization;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

// Unit tests for COLLABHOST_ADMIN_KEY env-var precedence. The env var wins over
// Auth:AdminKey config; whitespace-only values are treated as unset.
//
// [Collection] isolates this suite from parallel test runs that might otherwise race on
// the process-wide COLLABHOST_ADMIN_KEY environment variable.
[Collection(nameof(AuthorizationRegistrationTests))]
public class AuthorizationRegistrationTests
{
    private const string _envVarName = "COLLABHOST_ADMIN_KEY";

    private static AuthorizationSettings BuildSettings(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddCollabhostAuthorization(configuration);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<AuthorizationSettings>>().Value;
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithAdminKey(string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection
            (
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Auth:AdminKey"] = value
                }
            )
            .Build();

    [Fact]
    public void AddCollabhostAuthorization_EnvVarSet_WinsOverConfig()
    {
        const string EnvKey = "01ENV0ADMIN0KEY00000000000";
        const string ConfigKey = "01CONFIG0ADMIN0KEY00000000";

        Environment.SetEnvironmentVariable(_envVarName, EnvKey);

        try
        {
            var settings = BuildSettings(ConfigWithAdminKey(ConfigKey));

            settings.AdminKey.ShouldBe(EnvKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVarName, null);
        }
    }

    [Fact]
    public void AddCollabhostAuthorization_EnvVarUnset_FallsBackToConfig()
    {
        const string ConfigKey = "01CONFIG0ADMIN0KEY00000000";

        Environment.SetEnvironmentVariable(_envVarName, null);

        var settings = BuildSettings(ConfigWithAdminKey(ConfigKey));

        settings.AdminKey.ShouldBe(ConfigKey);
    }

    [Fact]
    public void AddCollabhostAuthorization_EnvVarUnsetConfigUnset_AdminKeyIsNull()
    {
        // Unset is represented as null -- UserSeedService handles generation in Scenario 1.
        // Registration-layer policy: do not generate here; leave AdminKey null and let the
        // seed path own admin-key policy.
        Environment.SetEnvironmentVariable(_envVarName, null);

        var settings = BuildSettings(EmptyConfig());

        settings.AdminKey.ShouldBeNull();
    }

    [Fact]
    public void AddCollabhostAuthorization_EnvVarWhitespace_FallsBackToConfig()
    {
        const string ConfigKey = "01CONFIG0ADMIN0KEY00000000";

        Environment.SetEnvironmentVariable(_envVarName, "   ");

        try
        {
            var settings = BuildSettings(ConfigWithAdminKey(ConfigKey));

            // Whitespace env var is treated as unset -- mirrors Phase 3 readers
            settings.AdminKey.ShouldBe(ConfigKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVarName, null);
        }
    }

    [Fact]
    public void AddCollabhostAuthorization_EnvVarEmpty_FallsBackToConfig()
    {
        const string ConfigKey = "01CONFIG0ADMIN0KEY00000000";

        Environment.SetEnvironmentVariable(_envVarName, "");

        try
        {
            var settings = BuildSettings(ConfigWithAdminKey(ConfigKey));

            settings.AdminKey.ShouldBe(ConfigKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable(_envVarName, null);
        }
    }
}

[CollectionDefinition(nameof(AuthorizationRegistrationTests), DisableParallelization = true)]
public class AuthorizationRegistrationTestsCollection { }
