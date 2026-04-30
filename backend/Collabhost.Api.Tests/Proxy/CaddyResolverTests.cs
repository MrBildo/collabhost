using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Precedence chain tests: env > appsettings > null.
// Tests manipulate COLLABHOST_CADDY_PATH via Environment.SetEnvironmentVariable
// and restore in try/finally to avoid polluting sibling tests.
public class CaddyResolverTests
{
    private static ProxySettings DefaultSettings(string? binaryPath = null) =>
        new()
        {
            BaseDomain = "collab.internal",
            BinaryPath = binaryPath,
            ListenAddress = ":443",
            CertLifetime = "168h"
        };

    // --- Env-var branch ---

    [Fact]
    public void Resolve_EnvVarSetAndExists_ReturnsEnvVarPath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"caddy-test-{Guid.NewGuid():N}");
        File.WriteAllText(tempFile, "fake");

        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, tempFile);

        try
        {
            var result = CaddyResolver.Resolve(DefaultSettings(), NullLogger.Instance);

            result.ShouldBe(Path.GetFullPath(tempFile));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Resolve_EnvVarSetButMissingFile_FallsThrough()
    {
        // Env var points at a nonexistent path. BinaryPath setting also unresolvable.
        // With nothing else configured, the resolver returns null.
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}"));

        try
        {
            var result = CaddyResolver.Resolve(DefaultSettings("nonexistent-binary-12345"), NullLogger.Instance);

            result.ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);
        }
    }

    [Fact]
    public void Resolve_EnvVarWhitespace_FallsThroughToConfig()
    {
        // A whitespace env var must be treated as unset. Verify by proving the appsettings
        // branch runs: supply an absolute-path entry pointing at a known temp file, assert
        // resolution succeeds.
        var tempFile = Path.Combine(Path.GetTempPath(), $"caddy-test-{Guid.NewGuid():N}");
        File.WriteAllText(tempFile, "fake");

        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, "   ");

        try
        {
            var result = CaddyResolver.Resolve(DefaultSettings(tempFile), NullLogger.Instance);

            result.ShouldBe(Path.GetFullPath(tempFile));
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);
            File.Delete(tempFile);
        }
    }

    // --- Appsettings branch ---

    [Fact]
    public void Resolve_ConfigBinaryPathAbsoluteAndExists_ReturnsConfigPath()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var tempFile = Path.Combine(Path.GetTempPath(), $"caddy-test-{Guid.NewGuid():N}");
        File.WriteAllText(tempFile, "fake");

        try
        {
            var result = CaddyResolver.Resolve(DefaultSettings(tempFile), NullLogger.Instance);

            result.ShouldBe(Path.GetFullPath(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Resolve_ConfigBinaryPathBareName_FallsThrough()
    {
        // Card #196: bare-name PATH walking was removed. A bare name in Proxy:BinaryPath now
        // fails File.Exists and the resolver returns null (no further fallback after #178/#196
        // resolution-cleanup).
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings("caddy"), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathSetButUnresolvable_FallsThrough()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings("nonexistent-binary-12345"), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathEmpty_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        // BinaryPath empty string -- resolver skips the appsettings branch and returns null.
        var result = CaddyResolver.Resolve(DefaultSettings(""), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathWhitespace_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings("   "), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathNull_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings(null), NullLogger.Instance);

        result.ShouldBeNull();
    }

    // --- Nothing-configured branch ---

    [Fact]
    public void Resolve_NoEnvNoAppsettings_ReturnsNull()
    {
        // Smoke test for the "honest disabled" path: nothing in env, nothing in appsettings.
        // Resolver returns null and ProxyManager translates that to ProxyState.Disabled.
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings(), NullLogger.Instance);

        result.ShouldBeNull();
    }
}
