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
        // A whitespace env var must be treated as unset. Verify by proving the config branch
        // runs: supply an absolute-path config entry pointing at a known temp file, assert
        // resolution succeeds. This avoids any ambient PATH dependency.
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
        // Place a fake binary in an isolated temp directory and supply only that directory
        // as PATH to the child process. Verifies bare-name resolution without depending on
        // ambient runner PATH state -- which is what caused the intermittent CI failure.
        var tempDir = Path.Combine(Path.GetTempPath(), $"caddy-path-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var fakeName = OperatingSystem.IsWindows() ? "fakecaddy.exe" : "fakecaddy";
        var fakePath = Path.Combine(tempDir, fakeName);
        File.WriteAllText(fakePath, "fake");

        if (!OperatingSystem.IsWindows())
        {
            // where/which on Unix requires the file to be marked executable.
            File.SetUnixFileMode(fakePath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        // On Windows, where.exe resolves bare names against PATHEXT to determine which file
        // extensions count as executables. Clearing the child environment removes the
        // inherited PATHEXT, which causes 'where fakecaddy' to fail to match 'fakecaddy.exe'
        // (exits 1, "Could not find files for the given pattern(s)"). This was the root
        // cause of the Windows-CI failure on the first attempt at this test. We include a
        // standard PATHEXT here so the child process can resolve the executable. SystemRoot
        // is added as a belt-and-suspenders defense for Windows loader behaviour on stripped
        // environments (some Windows binaries fail to start without %SystemRoot%).
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = tempDir
        };

        if (OperatingSystem.IsWindows())
        {
            environment["PATHEXT"] = ".COM;.EXE;.BAT;.CMD";

            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");

            if (!string.IsNullOrEmpty(systemRoot))
            {
                environment["SystemRoot"] = systemRoot;
            }
        }

        try
        {
            var result = CaddyResolver.ResolveBinaryPathSetting("fakecaddy", environment);

            result.ShouldNotBeNull();
            result.ShouldContain("fakecaddy", Case.Insensitive);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
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
