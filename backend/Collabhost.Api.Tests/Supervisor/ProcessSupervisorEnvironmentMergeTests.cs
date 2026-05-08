using System.Collections.Frozen;

using Collabhost.Api.Supervisor;

using Microsoft.Extensions.Logging;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

// Covers card #253 -- supervisor-spawn warning when an environment provider
// shadows an operator-set capability-override env var. The shadow detection
// is the foot-gun catch identified in the #215 recon Q3: dual paths feeding
// the same key with different values silently lose the dashboard value.
public class ProcessSupervisorEnvironmentMergeTests
{
    [Fact]
    public void Merge_OperatorOverrideShadowedByDifferentProviderValue_EmitsWarning()
    {
        var capabilityVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLOUDFLARE_API_TOKEN"] = "operator-set-value"
        };

        var overrideKeys = OverrideKeys("CLOUDFLARE_API_TOKEN");

        var providers = new IProcessEnvironmentProvider[]
        {
            new StubProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLOUDFLARE_API_TOKEN"] = "host-env-value"
            })
        };

        var captureLogger = new CaptureLogger();

        var result = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            providers,
            "proxy",
            captureLogger
        );

        result["CLOUDFLARE_API_TOKEN"].ShouldBe("host-env-value");
        captureLogger.WarningCount.ShouldBe(1);
        captureLogger.LastWarning.ShouldContain("CLOUDFLARE_API_TOKEN");
        captureLogger.LastWarning.ShouldContain("StubProvider"); // GetType().Name on the test stub
    }

    [Fact]
    public void Merge_OperatorOverrideAndProviderAgreeOnValue_NoWarning()
    {
        var capabilityVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CLOUDFLARE_API_TOKEN"] = "shared-value"
        };

        var overrideKeys = OverrideKeys("CLOUDFLARE_API_TOKEN");

        var providers = new IProcessEnvironmentProvider[]
        {
            new StubProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLOUDFLARE_API_TOKEN"] = "shared-value"
            })
        };

        var captureLogger = new CaptureLogger();

        var result = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            providers,
            "proxy",
            captureLogger
        );

        result["CLOUDFLARE_API_TOKEN"].ShouldBe("shared-value");
        captureLogger.WarningCount.ShouldBe(0);
    }

    [Fact]
    public void Merge_EmptyCapabilityDefaultsAndProviderOnly_NoWarning()
    {
        // Operator never opted into the key via the dashboard, and the type's
        // environment-defaults binding is empty -- no shadow, just the provider
        // doing its job.
        IDictionary<string, string>? capabilityVariables = null;
        var overrideKeys = FrozenSet<string>.Empty;

        var providers = new IProcessEnvironmentProvider[]
        {
            new StubProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLOUDFLARE_API_TOKEN"] = "host-env-value"
            })
        };

        var captureLogger = new CaptureLogger();

        var result = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            providers,
            "proxy",
            captureLogger
        );

        result["CLOUDFLARE_API_TOKEN"].ShouldBe("host-env-value");
        captureLogger.WarningCount.ShouldBe(0);
    }

    [Fact]
    public void Merge_ProviderContributesKeyOperatorDidNotOverride_NoWarning()
    {
        // Operator-set keys exist (e.g. "FOO" via dashboard), but the provider
        // contributes a different key the operator never touched. No shadow.
        var capabilityVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FOO"] = "operator-set-foo"
        };

        var overrideKeys = OverrideKeys("FOO");

        var providers = new IProcessEnvironmentProvider[]
        {
            new StubProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CLOUDFLARE_API_TOKEN"] = "host-env-value"
            })
        };

        var captureLogger = new CaptureLogger();

        var result = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            providers,
            "proxy",
            captureLogger
        );

        result["FOO"].ShouldBe("operator-set-foo");
        result["CLOUDFLARE_API_TOKEN"].ShouldBe("host-env-value");
        captureLogger.WarningCount.ShouldBe(0);
    }

    [Fact]
    public void Merge_TypeLevelDefaultMatchesProviderKey_NoWarning()
    {
        // Edge case: a type-level default (NOT an operator override) supplies the
        // same key the provider contributes with a different value. Per the spec,
        // we do NOT warn here -- only operator-explicit-set keys count.
        var capabilityVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TYPE_DEFAULT_KEY"] = "type-default-value"
        };

        // Note: overrideKeys is EMPTY -- the type default supplied the key, not the operator.
        var overrideKeys = FrozenSet<string>.Empty;

        var providers = new IProcessEnvironmentProvider[]
        {
            new StubProvider(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TYPE_DEFAULT_KEY"] = "provider-different-value"
            })
        };

        var captureLogger = new CaptureLogger();

        var result = ProcessSupervisor.MergeEnvironmentVariables
        (
            capabilityVariables,
            overrideKeys,
            providers,
            "proxy",
            captureLogger
        );

        result["TYPE_DEFAULT_KEY"].ShouldBe("provider-different-value");
        captureLogger.WarningCount.ShouldBe(0);
    }

    // Builds an operator-override key set without tripping CA1861 (which flags
    // inline array literals passed to extension methods). Each test wants its
    // own per-test set, so a static-readonly field per scenario doesn't fit
    // and a helper is the cleanest workaround.
    private static FrozenSet<string> OverrideKeys(params string[] keys) =>
        keys.ToFrozenSet(StringComparer.Ordinal);
}

// File-scoped stub matching IProcessEnvironmentProvider. Returns its given
// contributions verbatim regardless of slug -- the slug-filter is the
// concrete provider's responsibility, not the merge surface's.
//
// The warning message uses provider.GetType().Name; on a file-scoped class
// the compiler mangles the runtime name (e.g. "<filename>F<hash>__StubProvider")
// but it still contains "StubProvider" as a substring, which is what the
// shadow-warning assertions match against.
file sealed class StubProvider(IReadOnlyDictionary<string, string> contributions) : IProcessEnvironmentProvider
{
    public IReadOnlyDictionary<string, string> ContributeEnvironment(string appSlug) => contributions;
}

// Minimal capture logger -- mirrors the shape used in ProxyEnvironmentProviderTests.
// Records warning count and the last formatted warning message for assertion.
file sealed class CaptureLogger : ILogger
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
