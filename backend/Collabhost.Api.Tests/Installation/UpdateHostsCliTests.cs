using Collabhost.Api.Data;
using Collabhost.Api.Installation;
using Collabhost.Api.Registry;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Installation;

public class UpdateHostsCliTests : IAsyncLifetime
{
    private string _scratchDir = string.Empty;
    private string _dataDir = string.Empty;
    private string _hostsPath = string.Empty;

    public async Task InitializeAsync()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"collabhost-update-hosts-{Guid.NewGuid():N}");
        _dataDir = Path.Combine(_scratchDir, "data");
        Directory.CreateDirectory(_dataDir);
        _hostsPath = Path.Combine(_scratchDir, "hosts");

        // Pre-seed an existing hosts file so CLI's "missing-file" branch doesn't fire.
        await File.WriteAllTextAsync(_hostsPath, "127.0.0.1\tlocalhost\n");

        // Build an empty SQLite DB at the location HostsFileResolver looks for it.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dataDir, "collabhost.db")}")
                .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        // Point the CLI's config-load path at our scratch data dir via env var.
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", _dataDir);
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", _scratchDir);
    }

    public Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", null);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Run_NoArgs_EmptyAppStore_WritesPortalEntryOnly()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", _hostsPath],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);

        var content = await File.ReadAllTextAsync(_hostsPath);
        content.ShouldContain("collabhost.collab.internal");
        content.ShouldContain(HostsFileEditor.BeginMarker);
        content.ShouldContain(HostsFileEditor.EndMarker);
        // Pre-existing localhost line preserved outside the block.
        content.ShouldContain("127.0.0.1\tlocalhost");
    }

    [Fact]
    public async Task Run_DryRun_DoesNotWriteFile()
    {
        var before = await File.ReadAllTextAsync(_hostsPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", _hostsPath, "--dry-run"],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);

        var after = await File.ReadAllTextAsync(_hostsPath);
        after.ShouldBe(before);

        // Dry-run prints the would-be block to stdout.
        var output = stdout.ToString();
        output.ShouldContain("dry-run");
        output.ShouldContain(HostsFileEditor.BeginMarker);
        output.ShouldContain("collabhost.collab.internal");
    }

    [Fact]
    public async Task Run_IdempotentReRun_ReportsNoChanges()
    {
        // First run lays down the block.
        await UpdateHostsCli.RunAsync(["--hosts-path", _hostsPath], new StringWriter(), new StringWriter());

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", _hostsPath],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);
        stdout.ToString().ShouldContain("no changes");
    }

    [Fact]
    public async Task Run_AfterAppRegistered_BlockGainsNewSlug()
    {
        // Empty-state run.
        await UpdateHostsCli.RunAsync(["--hosts-path", _hostsPath], new StringWriter(), new StringWriter());

        await SeedAppAsync("freshly-added");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", _hostsPath],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);

        var content = await File.ReadAllTextAsync(_hostsPath);
        content.ShouldContain("freshly-added.collab.internal");
        stdout.ToString().ShouldContain("updated");
    }

    [Fact]
    public async Task Run_AppRemoved_BlockNoLongerListsIt()
    {
        await SeedAppAsync("doomed-app");
        await UpdateHostsCli.RunAsync(["--hosts-path", _hostsPath], new StringWriter(), new StringWriter());

        var firstContent = await File.ReadAllTextAsync(_hostsPath);
        firstContent.ShouldContain("doomed-app.collab.internal");

        await DeleteAppAsync("doomed-app");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", _hostsPath],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);

        var content = await File.ReadAllTextAsync(_hostsPath);
        content.ShouldNotContain("doomed-app.collab.internal");
    }

    [Fact]
    public async Task Run_UnknownArg_ReturnsUsageExitCode()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--bogus-flag"],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitUsage);
        stderr.ToString().ShouldContain("usage:");
    }

    [Fact]
    public async Task Run_HostsPathMissing_AndNotDryRun_ReturnsMissingHostsFileCode()
    {
        var missing = Path.Combine(_scratchDir, "definitely-not-here", "hosts");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", missing],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitMissingHostsFile);
    }

    [Fact]
    public async Task Run_DryRunOnMissingFile_StillReturnsOkWithComputedBlock()
    {
        // Dry-run path should NOT require the hosts file to exist -- operators staging a fresh
        // host want to preview the block before any file shows up.
        var missing = Path.Combine(_scratchDir, "missing-but-okay-for-dryrun", "hosts");

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await UpdateHostsCli.RunAsync
        (
            ["--hosts-path", missing, "--dry-run"],
            stdout,
            stderr
        );

        exit.ShouldBe(UpdateHostsCli.ExitOk);
        stdout.ToString().ShouldContain("collabhost.collab.internal");
    }

    private async Task SeedAppAsync(string slug)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dataDir, "collabhost.db")}")
                .Options;

        await using var db = new AppDbContext(options);

        db.Apps.Add(new App
        {
            Slug = slug,
            DisplayName = slug,
            AppTypeSlug = "static-site"
        });

        await db.SaveChangesAsync();
    }

    private async Task DeleteAppAsync(string slug)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_dataDir, "collabhost.db")}")
                .Options;

        await using var db = new AppDbContext(options);

        var app = await db.Apps.SingleAsync(a => a.Slug == slug);

        db.Apps.Remove(app);

        await db.SaveChangesAsync();
    }
}
