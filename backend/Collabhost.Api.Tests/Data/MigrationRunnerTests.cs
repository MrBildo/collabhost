using System.Globalization;

using Collabhost.Api.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class MigrationRunnerTests : IAsyncDisposable
{
    private readonly string _dataDirectory;
    private readonly string _backupsDirectory;
    private readonly string _dbPath;
    private readonly TestDbContextFactory _dbFactory;
    private readonly MigrationRunner _runner;

    public MigrationRunnerTests()
    {
        _dataDirectory = Path.Combine
        (
            Path.GetTempPath(),
            $"collabhost-migrunner-{Guid.NewGuid():N}"
        );

        Directory.CreateDirectory(_dataDirectory);

        _backupsDirectory = Path.Combine(_dataDirectory, "backups");
        Directory.CreateDirectory(_backupsDirectory);

        _dbPath = Path.Combine(_dataDirectory, "collabhost.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
                .Options;

        _dbFactory = new TestDbContextFactory(options);
        _runner = new MigrationRunner(_dbFactory, NullLogger<MigrationRunner>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;

        if (Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task MigrateWithBackupAsync_FirstRun_CreatesDbWithNoBackup()
    {
        File.Exists(_dbPath).ShouldBeFalse();

        var outcome = await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            fromSemver: "unknown",
            toSemver: "0.2.0",
            CancellationToken.None
        );

        outcome.Migrated.ShouldBeTrue();
        outcome.BackupPath.ShouldBeNull();
        outcome.AppliedMigrations.ShouldNotBeEmpty();

        File.Exists(_dbPath).ShouldBeTrue();
        Directory.EnumerateFiles(_backupsDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task MigrateWithBackupAsync_NoPendingMigrations_NoOpsWithNoBackup()
    {
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.2.0",
            CancellationToken.None
        );

        var outcome = await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "0.2.0",
            "0.2.0",
            CancellationToken.None
        );

        outcome.Migrated.ShouldBeFalse();
        outcome.BackupPath.ShouldBeNull();
        outcome.AppliedMigrations.ShouldBeEmpty();
        Directory.EnumerateFiles(_backupsDirectory).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPendingMigrationsAsync_OnEmptyDb_ReturnsAllMigrations()
    {
        var pending = await _runner.GetPendingMigrationsAsync(CancellationToken.None);

        pending.ShouldNotBeEmpty();
    }

    [Fact]
    public void BuildBackupPath_IncludesFromAndToSemver()
    {
        var path = MigrationRunner.BuildBackupPath(_backupsDirectory, "0.1.0", "0.2.0");

        var filename = Path.GetFileName(path);

        filename.ShouldStartWith("collabhost.db.bak-");
        filename.ShouldContain("-pre-0.1.0-to-0.2.0");
    }

    [Fact]
    public void BuildBackupPath_IncludesUnknownFromSemver()
    {
        var path = MigrationRunner.BuildBackupPath(_backupsDirectory, "unknown", "0.2.0");

        Path.GetFileName(path).ShouldContain("-pre-unknown-to-0.2.0");
    }

    [Fact]
    public void BuildBackupPath_UsesUtcTimestampFormat()
    {
        var path = MigrationRunner.BuildBackupPath(_backupsDirectory, "0.1.0", "0.2.0");

        var filename = Path.GetFileName(path);

        var afterPrefix = filename["collabhost.db.bak-".Length..];
        var zIndex = afterPrefix.IndexOf('Z', StringComparison.Ordinal);

        zIndex.ShouldBe(15);
        afterPrefix[8].ShouldBe('T');
    }

    [Fact]
    public void ResolveDbPath_ReturnsExpectedName() =>
        MigrationRunner.ResolveDbPath(_dataDirectory)
            .ShouldBe(Path.Combine(_dataDirectory, "collabhost.db"));

    [Fact]
    public async Task MigrateWithBackupAsync_NoPendingOnExistingDb_NoBackup()
    {
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.1.0",
            CancellationToken.None
        );

        File.Exists(_dbPath).ShouldBeTrue();

        var outcome = await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "0.1.0",
            "0.2.0",
            CancellationToken.None
        );

        outcome.Migrated.ShouldBeFalse();
        outcome.BackupPath.ShouldBeNull();
    }

    [Fact]
    public void TryPruneBackups_RetainsFiveMostRecent()
    {
        PlantBackups(count: 8);

        MigrationRunner.TryPruneBackups(_backupsDirectory, NullLogger<MigrationRunner>.Instance);

        var remaining = Directory.EnumerateFiles(_backupsDirectory).ToArray();

        remaining.Length.ShouldBe(MigrationRunner.BackupRetentionCount);
    }

    [Fact]
    public void TryPruneBackups_OnFewerThanRetentionCount_NoOp()
    {
        PlantBackups(count: 3);

        MigrationRunner.TryPruneBackups(_backupsDirectory, NullLogger<MigrationRunner>.Instance);

        Directory.EnumerateFiles(_backupsDirectory).Count().ShouldBe(3);
    }

    [Fact]
    public void TryPruneBackups_DeletesOldestFirst()
    {
        var oldestStamp = DateTime.UtcNow.AddMinutes(-100).ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var oldest = Path.Combine
        (
            _backupsDirectory,
            string.Concat(MigrationRunner.BackupFilePrefix, oldestStamp, "-pre-0.0.1-to-0.2.0")
        );

        File.WriteAllText(oldest, "oldest");
        File.SetCreationTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-100));

        PlantBackups(count: 5);

        MigrationRunner.TryPruneBackups(_backupsDirectory, NullLogger<MigrationRunner>.Instance);

        File.Exists(oldest).ShouldBeFalse();
        Directory.EnumerateFiles(_backupsDirectory).Count().ShouldBe(5);
    }

    [Fact]
    public async Task MigrateWithBackupAsync_ThrowsOnEmptyDataDirectory() =>
        await Should.ThrowAsync<ArgumentException>
        (
            () => _runner.MigrateWithBackupAsync
            (
                string.Empty,
                _backupsDirectory,
                "unknown",
                "0.2.0",
                CancellationToken.None
            )
        );

    [Fact]
    public async Task MigrateWithBackupAsync_ThrowsOnEmptyToSemver() =>
        await Should.ThrowAsync<ArgumentException>
        (
            () => _runner.MigrateWithBackupAsync
            (
                _dataDirectory,
                _backupsDirectory,
                "unknown",
                string.Empty,
                CancellationToken.None
            )
        );

    private void PlantBackups(int count)
    {
        for (var index = 0; index < count; index++)
        {
            var stamp = DateTime.UtcNow.AddMinutes(-index)
                .ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

            var suffix = index.ToString(CultureInfo.InvariantCulture);
            var filename = string.Concat
            (
                MigrationRunner.BackupFilePrefix,
                stamp,
                "-pre-0.0.",
                suffix,
                "-to-0.2.0"
            );

            var path = Path.Combine(_backupsDirectory, filename);

            File.WriteAllText(path, "stub-" + suffix);
            File.SetCreationTimeUtc(path, DateTime.UtcNow.AddMinutes(-index));
        }
    }
}

// Sealed: test helper only, no subtype need
internal sealed class TestDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);

#pragma warning disable VSTHRD200 // Interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(options));
#pragma warning restore VSTHRD200
}
