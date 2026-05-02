using System.Globalization;

using Collabhost.Api.Platform;

using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class CrashLogTests : IDisposable
{
    private readonly string _tempRoot;

    public CrashLogTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"collabhost-crashlog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        // Clear in case a prior test leaked.
        Environment.SetEnvironmentVariable(CrashLog.EnvironmentVariableName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CrashLog.EnvironmentVariableName, null);

        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, true);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; not the test's responsibility.
            }
        }

        GC.SuppressFinalize(this);
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [key] = value })
            .Build();

    [Fact]
    public void ResolveDirectory_NoEnvNoConfig_DefaultsToDataDirSlashLogs()
    {
        var dataDir = Path.Combine(_tempRoot, "data");

        var resolved = CrashLog.ResolveDirectory(EmptyConfig(), dataDir);

        resolved.ShouldBe(Path.Combine(dataDir, CrashLog.DefaultDirectoryName));
    }

    [Fact]
    public void ResolveDirectory_EnvVarSet_WinsOverConfigAndDefault()
    {
        var envOverride = Path.Combine(_tempRoot, "env-logs");
        var configValue = Path.Combine(_tempRoot, "config-logs");

        Environment.SetEnvironmentVariable(CrashLog.EnvironmentVariableName, envOverride);

        var resolved = CrashLog.ResolveDirectory
        (
            ConfigWith(CrashLog.ConfigurationDirectoryKey, configValue),
            Path.Combine(_tempRoot, "data")
        );

        resolved.ShouldBe(envOverride);
    }

    [Fact]
    public void ResolveDirectory_ConfigSet_WinsOverDefault()
    {
        var configValue = Path.Combine(_tempRoot, "config-logs");

        var resolved = CrashLog.ResolveDirectory
        (
            ConfigWith(CrashLog.ConfigurationDirectoryKey, configValue),
            Path.Combine(_tempRoot, "data")
        );

        resolved.ShouldBe(configValue);
    }

    [Fact]
    public void ResolveRetention_NoConfig_ReturnsDefault() =>
        CrashLog.ResolveRetention(EmptyConfig()).ShouldBe(CrashLog.DefaultRetention);

    [Fact]
    public void ResolveRetention_PositiveValue_ReturnsParsed()
    {
        var config = ConfigWith(CrashLog.ConfigurationRetentionKey, "5");

        CrashLog.ResolveRetention(config).ShouldBe(5);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void ResolveRetention_InvalidValue_FallsBackToDefault(string raw)
    {
        var config = ConfigWith(CrashLog.ConfigurationRetentionKey, raw);

        CrashLog.ResolveRetention(config).ShouldBe(CrashLog.DefaultRetention);
    }

    [Fact]
    public void TryWrite_OnFreshDirectory_CreatesFileWithExpectedName()
    {
        var dir = Path.Combine(_tempRoot, "fresh");
        var stamp = new DateTimeOffset(2026, 5, 1, 12, 34, 56, TimeSpan.Zero);

        var path = CrashLog.TryWrite(dir, stamp, "hello world", retention: CrashLog.DefaultRetention);

        path.ShouldNotBeNull();
        File.Exists(path).ShouldBeTrue();
        Path.GetFileName(path).ShouldStartWith(CrashLog.FilenamePrefix);
        Path.GetFileName(path).ShouldEndWith(CrashLog.FilenameExtension);
        Path.GetFileName(path).ShouldContain("20260501T123456Z");

        File.ReadAllText(path).ShouldBe("hello world");
    }

    [Fact]
    public void BuildContent_IncludesSummaryDetailsRecoveryAndExitCode()
    {
        var stamp = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var content = CrashLog.BuildContent
        (
            stamp,
            "preflight failed",
            [("Path", "/data"), ("Error", "permission denied")],
            ["Check permissions.", "Use COLLABHOST_DATA_PATH override."],
            10
        );

        content.ShouldContain("Collabhost crash log");
        content.ShouldContain("preflight failed");
        content.ShouldContain("Path: /data");
        content.ShouldContain("Error: permission denied");
        content.ShouldContain("1. Check permissions.");
        content.ShouldContain("2. Use COLLABHOST_DATA_PATH override.");
        content.ShouldContain("Exit code: 10");
        content.ShouldContain("2026-05-01");
    }

    [Fact]
    public void BuildContent_WithException_AppendsExceptionBlock()
    {
        var ex = new InvalidOperationException("kaboom");
        var stamp = DateTimeOffset.UtcNow;

        var content = CrashLog.BuildContent
        (
            stamp,
            "boom",
            [],
            [],
            1,
            ex
        );

        content.ShouldContain("Exception:");
        content.ShouldContain("InvalidOperationException");
        content.ShouldContain("kaboom");
    }

    [Fact]
    public void ApplyRetention_WhenOverLimit_PrunesOldestFirst()
    {
        var dir = Path.Combine(_tempRoot, "retention");
        Directory.CreateDirectory(dir);

        // Six files, write times 1..6 minutes in the past. Retention=3 should leave the
        // three most-recent (offsets 1, 2, 3 minutes ago).
        var now = DateTime.UtcNow;
        var files = new List<string>();

        for (var index = 1; index <= 6; index++)
        {
            var label = index.ToString(CultureInfo.InvariantCulture);
            var name = $"{CrashLog.FilenamePrefix}stamp-{label}{CrashLog.FilenameExtension}";
            var path = Path.Combine(dir, name);

            File.WriteAllText(path, $"file {label}");
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-index));

            files.Add(path);
        }

        CrashLog.ApplyRetention(dir, retention: 3);

        File.Exists(files[0]).ShouldBeTrue();  // -1 min (newest)
        File.Exists(files[1]).ShouldBeTrue();  // -2 min
        File.Exists(files[2]).ShouldBeTrue();  // -3 min
        File.Exists(files[3]).ShouldBeFalse(); // -4 min (pruned)
        File.Exists(files[4]).ShouldBeFalse(); // -5 min (pruned)
        File.Exists(files[5]).ShouldBeFalse(); // -6 min (pruned)
    }

    [Fact]
    public void ApplyRetention_OnlyTouchesCrashLogFiles()
    {
        var dir = Path.Combine(_tempRoot, "mixed");
        Directory.CreateDirectory(dir);

        var unrelatedPath = Path.Combine(dir, "operator-notes.txt");
        File.WriteAllText(unrelatedPath, "do not delete");

        // 5 crash files, retention=2 prunes 3 of them. Unrelated file untouched.
        var now = DateTime.UtcNow;

        for (var index = 1; index <= 5; index++)
        {
            var label = index.ToString(CultureInfo.InvariantCulture);
            var name = $"{CrashLog.FilenamePrefix}stamp-{label}{CrashLog.FilenameExtension}";
            var path = Path.Combine(dir, name);

            File.WriteAllText(path, $"file {label}");
            File.SetLastWriteTimeUtc(path, now.AddMinutes(-index));
        }

        CrashLog.ApplyRetention(dir, retention: 2);

        File.Exists(unrelatedPath).ShouldBeTrue();
        Directory.GetFiles(dir, $"{CrashLog.FilenamePrefix}*{CrashLog.FilenameExtension}").Length.ShouldBe(2);
    }

    [Fact]
    public void TryWrite_OnInvalidDirectory_ReturnsNull()
    {
        // Embedded null character is invalid on every OS we care about.
        var bogus = "\0invalid";

        var path = CrashLog.TryWrite(bogus, DateTimeOffset.UtcNow, "content", retention: 5);

        path.ShouldBeNull();
    }
}
