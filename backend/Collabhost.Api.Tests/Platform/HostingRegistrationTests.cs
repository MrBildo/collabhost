using Collabhost.Api.Platform;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class HostingRegistrationTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWithHosting(params (string key, string value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection
            (
                entries.ToDictionary
                (
                    e => $"Hosting:{e.key}",
                    e => (string?)e.value,
                    StringComparer.Ordinal
                )
            )
            .Build();

    // --- COLLABHOST_HOSTING_LISTEN_PORT ---

    [Fact]
    public void ResolveSettings_ListenPortEnvVarSet_WinsOverConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", "9000");

        try
        {
            var config = ConfigWithHosting(("ListenPort", "58400"));

            var result = HostingRegistration.ResolveSettings(config);

            result.ListenPort.ShouldBe(9000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);
        }
    }

    [Fact]
    public void ResolveSettings_ListenPortEnvVarUnset_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);

        var config = ConfigWithHosting(("ListenPort", "7000"));

        var result = HostingRegistration.ResolveSettings(config);

        result.ListenPort.ShouldBe(7000);
    }

    [Fact]
    public void ResolveSettings_ListenPortEnvVarUnsetConfigUnset_ReturnsHardcodedDefault()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);

        var result = HostingRegistration.ResolveSettings(EmptyConfig());

        result.ListenPort.ShouldBe(58400);
    }

    [Fact]
    public void ResolveSettings_ListenPortEnvVarInvalid_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", "not-a-number");

        try
        {
            var config = ConfigWithHosting(("ListenPort", "58400"));

            var result = HostingRegistration.ResolveSettings(config);

            result.ListenPort.ShouldBe(58400);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);
        }
    }

    [Fact]
    public void ResolveSettings_ListenPortEnvVarOutOfRange_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", "99999");

        try
        {
            var config = ConfigWithHosting(("ListenPort", "58400"));

            var result = HostingRegistration.ResolveSettings(config);

            result.ListenPort.ShouldBe(58400);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);
        }
    }

    [Fact]
    public void ResolveSettings_ListenPortEnvVarWhitespace_FallsBackToConfig()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", "   ");

        try
        {
            var config = ConfigWithHosting(("ListenPort", "58400"));

            var result = HostingRegistration.ResolveSettings(config);

            result.ListenPort.ShouldBe(58400);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COLLABHOST_HOSTING_LISTEN_PORT", null);
        }
    }
}
