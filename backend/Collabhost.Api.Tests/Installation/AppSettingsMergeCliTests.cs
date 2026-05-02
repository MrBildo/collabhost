using Collabhost.Api.Installation;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Installation;

public class AppSettingsMergeCliTests : IDisposable
{
    private readonly string _scratchDir;

    public AppSettingsMergeCliTests()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"collabhost-merge-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            try
            {
                Directory.Delete(_scratchDir, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Run_FullMerge_PreservesOperatorEditsRefreshesUntouchedAddsNewKeys()
    {
        var shippedPath = WriteJson("shipped.json",
            """
            {
              "Hosting": { "ListenPort": 58500 },
              "Portal": { "Subdomain": "collabhost" }
            }
            """);

        var currentPath = WriteJson("appsettings.json",
            """
            {
              "Hosting": { "ListenPort": 9090 },
              "Auth": { "AdminKey": "operator-key" }
            }
            """);

        var baselinePath = WriteJson("appsettings.shipped.json",
            """
            {
              "Hosting": { "ListenPort": 58400 },
              "Auth": { "AdminKey": null }
            }
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run
        (
            ["--baseline", baselinePath, shippedPath, currentPath],
            stdout,
            stderr
        );

        exit.ShouldBe(AppSettingsMergeCli.ExitOk);

        var merged = await File.ReadAllTextAsync(currentPath);
        merged.ShouldContain("\"ListenPort\": 9090");        // operator edit preserved
        merged.ShouldContain("\"AdminKey\": \"operator-key\""); // operator add preserved
        merged.ShouldContain("\"Subdomain\": \"collabhost\""); // new shipped key added

        // Baseline should have refreshed to the new shipped content (byte-for-byte).
        var baselineAfter = await File.ReadAllTextAsync(baselinePath);
        var shippedAfter = await File.ReadAllTextAsync(shippedPath);
        baselineAfter.ShouldBe(shippedAfter);

        var summary = stdout.ToString();
        summary.ShouldContain("updated");
        summary.ShouldContain("Portal");
    }

    [Fact]
    public async Task Run_FirstUpgradeNoBaseline_UsesConservativeModeButStillAddsNewKeys()
    {
        var shippedPath = WriteJson("shipped.json",
            """
            {
              "Hosting": { "ListenPort": 58500 },
              "Portal": { "Subdomain": "collabhost" }
            }
            """);

        var currentPath = WriteJson("appsettings.json",
            """{"Hosting":{"ListenPort":58400}}""");

        var baselinePath = Path.Combine(_scratchDir, "appsettings.shipped.json");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run
        (
            ["--baseline", baselinePath, shippedPath, currentPath],
            stdout,
            stderr
        );

        exit.ShouldBe(AppSettingsMergeCli.ExitOk);

        var merged = await File.ReadAllTextAsync(currentPath);
        merged.ShouldContain("\"ListenPort\": 58400");          // existing value preserved (conservative)
        merged.ShouldContain("\"Subdomain\": \"collabhost\"");  // new key added

        // Baseline created on first run with the shipped content -- next merge can refresh.
        File.Exists(baselinePath).ShouldBeTrue();

        stdout.ToString().ShouldContain("conservative mode");
    }

    [Fact]
    public void Run_NoArgs_ReturnsUsageExitCode()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run([], stdout, stderr);

        exit.ShouldBe(AppSettingsMergeCli.ExitUsage);
        stderr.ToString().ShouldContain("usage:");
    }

    [Fact]
    public async Task Run_MissingShippedFile_ReturnsErrorAndDoesNotTouchOnDisk()
    {
        var currentPath = WriteJson("appsettings.json", """{"Hosting":{"ListenPort":58400}}""");
        var ondiskBefore = await File.ReadAllTextAsync(currentPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run
        (
            [Path.Combine(_scratchDir, "missing-shipped.json"), currentPath],
            stdout,
            stderr
        );

        exit.ShouldBe(AppSettingsMergeCli.ExitMissingShippedFile);
        (await File.ReadAllTextAsync(currentPath)).ShouldBe(ondiskBefore);
    }

    [Fact]
    public async Task Run_CorruptOnDiskFile_LeavesItUntouched()
    {
        var shippedPath = WriteJson("shipped.json", """{"Hosting":{"ListenPort":58400}}""");
        var currentPath = Path.Combine(_scratchDir, "appsettings.json");
        await File.WriteAllTextAsync(currentPath, "not json {{");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run([shippedPath, currentPath], stdout, stderr);

        exit.ShouldBe(AppSettingsMergeCli.ExitParseFailed);
        (await File.ReadAllTextAsync(currentPath)).ShouldBe("not json {{");
        stderr.ToString().ShouldContain("leaving on-disk file untouched");
    }

    [Fact]
    public async Task Run_CorruptBaseline_FallsBackToConservativeMergeAndStillSucceeds()
    {
        var shippedPath = WriteJson("shipped.json",
            """
            {
              "Hosting": { "ListenPort": 58500 },
              "NewKey": "value"
            }
            """);

        var currentPath = WriteJson("appsettings.json",
            """{"Hosting":{"ListenPort":9090}}""");

        var baselinePath = Path.Combine(_scratchDir, "appsettings.shipped.json");
        await File.WriteAllTextAsync(baselinePath, "garbage }}");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = AppSettingsMergeCli.Run
        (
            ["--baseline", baselinePath, shippedPath, currentPath],
            stdout,
            stderr
        );

        exit.ShouldBe(AppSettingsMergeCli.ExitOk);

        var merged = await File.ReadAllTextAsync(currentPath);
        merged.ShouldContain("\"ListenPort\": 9090");      // conservative preservation
        merged.ShouldContain("\"NewKey\": \"value\"");      // still adds new keys

        stderr.ToString().ShouldContain("ignoring corrupt baseline");
    }

    private string WriteJson(string fileName, string content)
    {
        var path = Path.Combine(_scratchDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
