using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Each test isolates its env-var mutations to its own variable name to avoid
// cross-test contamination via the process-scoped Environment table.
public class ProxyEnvironmentProviderTests
{
    [Fact]
    public void ContributeEnvironment_NonProxySlug_ReturnsEmpty()
    {
        var settings = CreateSettings(dnsProvider: "cloudflare", tokenEnvVar: "CFTEST_TOKEN_NONPROXY");

        Environment.SetEnvironmentVariable("CFTEST_TOKEN_NONPROXY", "secret");

        try
        {
            var provider = CreateProvider(settings);

            var result = provider.ContributeEnvironment("my-web-app");

            result.ShouldBeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CFTEST_TOKEN_NONPROXY", null);
        }
    }

    [Fact]
    public void ContributeEnvironment_ProxySlug_DnsProviderUnset_ReturnsEmpty()
    {
        // Internal-CA branch -- nothing to contribute.
        var settings = CreateSettings(dnsProvider: null, tokenEnvVar: "CLOUDFLARE_API_TOKEN");

        var provider = CreateProvider(settings);

        var result = provider.ContributeEnvironment("proxy");

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ContributeEnvironment_ProxySlug_TokenPresent_ContributesToken()
    {
        const string EnvVarName = "CFTEST_TOKEN_PRESENT";
        const string TokenValue = "cf-fake-token-value";

        var settings = CreateSettings(dnsProvider: "cloudflare", tokenEnvVar: EnvVarName);

        Environment.SetEnvironmentVariable(EnvVarName, TokenValue);

        try
        {
            var provider = CreateProvider(settings);

            var result = provider.ContributeEnvironment("proxy");

            result.Count.ShouldBe(1);
            result[EnvVarName].ShouldBe(TokenValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVarName, null);
        }
    }

    [Fact]
    public void ContributeEnvironment_ProxySlug_TokenAbsent_ReturnsEmptyAndLogsWarning()
    {
        const string EnvVarName = "CFTEST_TOKEN_ABSENT";

        // Defensive: scrub in case a prior failed run left state behind.
        Environment.SetEnvironmentVariable(EnvVarName, null);

        var settings = CreateSettings(dnsProvider: "cloudflare", tokenEnvVar: EnvVarName);

        var captureLogger = new CaptureLogger<ProxyEnvironmentProvider>();
        var provider = new ProxyEnvironmentProvider(settings, captureLogger);

        var result = provider.ContributeEnvironment("proxy");

        result.ShouldBeEmpty();
        captureLogger.WarningCount.ShouldBe(1);
        captureLogger.LastWarning.ShouldContain(EnvVarName);
    }

    [Fact]
    public void ContributeEnvironment_ProxySlug_TokenEnvVarNameEmpty_ReturnsEmptyAndLogsWarning()
    {
        var settings = CreateSettings(dnsProvider: "cloudflare", tokenEnvVar: "");

        var captureLogger = new CaptureLogger<ProxyEnvironmentProvider>();
        var provider = new ProxyEnvironmentProvider(settings, captureLogger);

        var result = provider.ContributeEnvironment("proxy");

        result.ShouldBeEmpty();
        captureLogger.WarningCount.ShouldBe(1);
    }

    private static ProxySettings CreateSettings(string? dnsProvider, string? tokenEnvVar) =>
        new()
        {
            BaseDomain = "collab.internal",
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            DnsProvider = dnsProvider,
            DnsApiTokenEnvVar = tokenEnvVar,
            AdminPort = 2019
        };

    private static ProxyEnvironmentProvider CreateProvider(ProxySettings settings) =>
        new(settings, NullLogger<ProxyEnvironmentProvider>.Instance);
}

// Minimal capture logger -- avoids pulling in NSubstitute for two warning-shape assertions.
file sealed class CaptureLogger<T> : ILogger<T>
{
    public int WarningCount { get; private set; }
    public string LastWarning { get; private set; } = "";

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>
    (
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (logLevel == LogLevel.Warning)
        {
            WarningCount++;
            LastWarning = formatter(state, exception);
        }
    }
}
