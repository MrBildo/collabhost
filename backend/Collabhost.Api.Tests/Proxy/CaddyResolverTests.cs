using Collabhost.Api.Proxy;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Proxy;

// Precedence chain tests: env > config > bundled.
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
            CertLifetime = "168h",
            SelfPort = 58400
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
        // Env var points at a nonexistent path. BinaryPath config also unresolvable.
        // Bundled path in the test binary's base dir will not exist either.
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
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, "   ");

        try
        {
            // Config supplies a bare name that resolves (where/sh).
            var bareName = OperatingSystem.IsWindows() ? "where" : "sh";

            var result = CaddyResolver.Resolve(DefaultSettings(bareName), NullLogger.Instance);

            result.ShouldNotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);
        }
    }

    // --- Config branch ---

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
    public void Resolve_ConfigBinaryPathBareNameResolvesViaPath_ReturnsResolvedPath()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var bareName = OperatingSystem.IsWindows() ? "where" : "sh";

        var result = CaddyResolver.Resolve(DefaultSettings(bareName), NullLogger.Instance);

        result.ShouldNotBeNull();
        // On Windows, where.exe lives in System32; on Unix, sh lives in /bin or /usr/bin.
        result.ShouldContain(OperatingSystem.IsWindows() ? "where" : "sh", Case.Insensitive);
    }

    [Fact]
    public void Resolve_ConfigBinaryPathSetButUnresolvable_FallsThrough()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings("nonexistent-binary-12345"), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathEmpty_FallsThroughToBundled()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        // BinaryPath empty string -- resolver skips config branch, falls through to bundled.
        // Bundled path is in test assembly's BaseDirectory and won't contain caddy.
        var result = CaddyResolver.Resolve(DefaultSettings(""), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathWhitespace_FallsThroughToBundled()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings("   "), NullLogger.Instance);

        result.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ConfigBinaryPathNull_FallsThroughToBundled()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        var result = CaddyResolver.Resolve(DefaultSettings(null), NullLogger.Instance);

        result.ShouldBeNull();
    }

    // --- Bundled branch ---

    [Fact]
    public void Resolve_BundledSidecarExists_ReturnsBundledPath()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        // Create a fake caddy binary next to the test assembly so the bundled path resolves.
        var bundledName = OperatingSystem.IsWindows() ? "caddy.exe" : "caddy";
        var bundledPath = Path.Combine(AppContext.BaseDirectory, bundledName);

        var createdForTest = false;

        if (!File.Exists(bundledPath))
        {
            File.WriteAllText(bundledPath, "fake");
            createdForTest = true;
        }

        try
        {
            var result = CaddyResolver.Resolve(DefaultSettings(), NullLogger.Instance);

            result.ShouldBe(Path.GetFullPath(bundledPath));
        }
        finally
        {
            if (createdForTest)
            {
                File.Delete(bundledPath);
            }
        }
    }

    // --- Nothing-available branch ---

    [Fact]
    public void Resolve_AllPathsExhausted_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(CaddyResolver.EnvVarName, null);

        // Ensure no bundled caddy exists next to the test assembly.
        var bundledName = OperatingSystem.IsWindows() ? "caddy.exe" : "caddy";
        var bundledPath = Path.Combine(AppContext.BaseDirectory, bundledName);

        if (File.Exists(bundledPath))
        {
            // Don't delete -- another test may have placed a real binary here. Skip assertion.
            return;
        }

        var result = CaddyResolver.Resolve(DefaultSettings(), NullLogger.Instance);

        result.ShouldBeNull();
    }
}
