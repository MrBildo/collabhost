using System.Globalization;

using Collabhost.Api.Data;

using Microsoft.Data.Sqlite;
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
                Directory.Delete(_dataDirectory, true);
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
    public async Task MigrateWithBackupAsync_WalResidentRow_SurvivesBackupRoundTrip()
    {
        // Seed the DB so a real file exists, then clear history so the next call backs up + migrates.
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.1.0",
            CancellationToken.None
        );

        const string probeValue = "wal-resident-canary";

        // Switch to WAL mode, disable auto-checkpoint, and commit a row through a connection we hold
        // OPEN across the backup. With auto-checkpoint off and the connection never closed, the row
        // stays resident in the `-wal` sidecar and is NOT folded into the main collabhost.db file --
        // exactly the condition under which a plain File.Copy of the main file would lose it.
        await using var walConnection = new SqliteConnection($"Data Source={_dbPath}");
        await walConnection.OpenAsync();

        await ExecuteAsync(walConnection, "PRAGMA journal_mode=WAL;");
        await ExecuteAsync(walConnection, "PRAGMA wal_autocheckpoint=0;");
        await ExecuteAsync(walConnection, "CREATE TABLE wal_probe (value TEXT NOT NULL);");
        await ExecuteAsync(walConnection, $"INSERT INTO wal_probe (value) VALUES ('{probeValue}');");

        // Negative control: the committed row lives in the WAL sidecar, not the main file.
        var walPath = _dbPath + "-wal";
        File.Exists(walPath).ShouldBeTrue("the committed row should be resident in the -wal sidecar");
        new FileInfo(walPath).Length.ShouldBeGreaterThan(0);

        // Clearing history makes the next call see pending migrations -> it takes the backup, then
        // re-applies migrations against a DB whose tables already exist and throws (exit 20). The
        // backup is taken BEFORE the migrate, so it carries the WAL-resident row regardless of the
        // migrate failure -- the exception is the established channel for the backup path here.
        ClearMigrationsHistory();

        var ex = await Should.ThrowAsync<MigrationFailedException>
        (
            () => _runner.MigrateWithBackupAsync
            (
                _dataDirectory,
                _backupsDirectory,
                "0.1.0",
                "0.2.0",
                CancellationToken.None
            )
        );

        ex.BackupPath.ShouldNotBeNull();
        File.Exists(ex.BackupPath!).ShouldBeTrue();

        // The backup must be a self-contained single file -- no WAL sidecar of its own to depend on.
        File.Exists(ex.BackupPath + "-wal").ShouldBeFalse();

        // Restore == open the backup and read the row back. If the WAL contents were captured, the
        // canary survives the backup; a main-file-only File.Copy would have lost it.
        await using var restored = new SqliteConnection($"Data Source={ex.BackupPath}");
        await restored.OpenAsync();

        await using var read = restored.CreateCommand();
        read.CommandText = "SELECT value FROM wal_probe;";

        var restoredValue = (string?)await read.ExecuteScalarAsync();

        restoredValue.ShouldBe(probeValue);
    }

    [Fact]
    public void TryPruneBackups_RetainsFiveMostRecent()
    {
        PlantBackups(8);

        MigrationRunner.TryPruneBackups(_backupsDirectory, NullLogger<MigrationRunner>.Instance);

        var remaining = Directory.EnumerateFiles(_backupsDirectory).ToArray();

        remaining.Length.ShouldBe(MigrationRunner.BackupRetentionCount);
    }

    [Fact]
    public void TryPruneBackups_OnFewerThanRetentionCount_NoOp()
    {
        PlantBackups(3);

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

        PlantBackups(5);

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

    [Fact]
    public async Task MigrateWithBackupAsync_BackupFilenameCollision_Exits11()
    {
        // Arrange: run first to create the DB (so a real file exists for File.Copy to target).
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.1.0",
            CancellationToken.None
        );

        // Force pending migrations by wiping the applied-migrations history so a second
        // MigrateWithBackupAsync invocation takes the backup + migrate branch.
        ClearMigrationsHistory();

        // BuildBackupPath uses DateTime.UtcNow to the second. Plant files matching each second
        // in a small window so the test is robust against boundary crossings (call takes ms).
        var fromSemver = "0.1.0";
        var toSemver = "0.2.0";
        var plantedPaths = PlantBackupsForTimeWindow(fromSemver, toSemver, seconds: 10);

        // Act
        var ex = await Should.ThrowAsync<MigrationFailedException>
        (
            () => _runner.MigrateWithBackupAsync
            (
                _dataDirectory,
                _backupsDirectory,
                fromSemver,
                toSemver,
                CancellationToken.None
            )
        );

        // Assert
        ex.ExitCode.ShouldBe(11);
        ex.Summary.ShouldContain("collision");
        ex.BackupPath.ShouldNotBeNull();

        // The runner should have targeted one of the planted paths.
        plantedPaths.ShouldContain(ex.BackupPath!);
    }

    [Fact]
    public async Task MigrateWithBackupAsync_DatabaseLocked_Exits11()
    {
        // Seed a real DB so File.Copy has a source to back up. Then clear __EFMigrationsHistory
        // so the runner sees pending migrations on the next call and enters the migrate branch.
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.1.0",
            CancellationToken.None
        );

        ClearMigrationsHistory();

        var factory = new ThrowingDbContextFactory
        (
            _dbFactory.Options,
            2,
            new SqliteException("database is locked", 5)
        );

        var runner = new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance);

        var ex = await Should.ThrowAsync<MigrationFailedException>
        (
            () => runner.MigrateWithBackupAsync
            (
                _dataDirectory,
                _backupsDirectory,
                "0.1.0",
                "0.2.0",
                CancellationToken.None
            )
        );

        ex.ExitCode.ShouldBe(11);
        ex.Summary.ShouldContain("locked");
        ex.BackupPath.ShouldNotBeNull();
    }

    [Fact]
    public async Task MigrateWithBackupAsync_GenericMigrateThrow_Exits20()
    {
        await _runner.MigrateWithBackupAsync
        (
            _dataDirectory,
            _backupsDirectory,
            "unknown",
            "0.1.0",
            CancellationToken.None
        );

        ClearMigrationsHistory();

        var factory = new ThrowingDbContextFactory
        (
            _dbFactory.Options,
            2,
            new InvalidOperationException("planted migration failure")
        );

        var runner = new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance);

        var ex = await Should.ThrowAsync<MigrationFailedException>
        (
            () => runner.MigrateWithBackupAsync
            (
                _dataDirectory,
                _backupsDirectory,
                "0.1.0",
                "0.2.0",
                CancellationToken.None
            )
        );

        ex.ExitCode.ShouldBe(20);
        ex.Summary.ShouldContain("migration failed");
        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
        ex.BackupPath.ShouldNotBeNull();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private void ClearMigrationsHistory()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");

        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM __EFMigrationsHistory";
        command.ExecuteNonQuery();
    }

    private List<string> PlantBackupsForTimeWindow(string fromSemver, string toSemver, int seconds)
    {
        var planted = new List<string>();
        var now = DateTime.UtcNow;

        for (var offset = -1; offset < seconds; offset++)
        {
            var stamp = now.AddSeconds(offset)
                .ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

            var filename = string.Concat
            (
                MigrationRunner.BackupFilePrefix,
                stamp,
                "-pre-",
                fromSemver,
                "-to-",
                toSemver
            );

            var path = Path.Combine(_backupsDirectory, filename);

            File.WriteAllText(path, "planted");
            planted.Add(path);
        }

        return planted;
    }

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
    public DbContextOptions<AppDbContext> Options { get; } = options;

    public AppDbContext CreateDbContext() => new(Options);

#pragma warning disable VSTHRD200 // Interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new AppDbContext(Options));
#pragma warning restore VSTHRD200
}

// Sealed: test helper only, no subtype need.
// Call 1 returns a real context so GetPendingMigrationsAsync succeeds and reports pending items.
// Call N (configured via throwOnCall) throws the configured exception to drive the runner's
// catch branches for DB-locked (exit 11) and generic migration failure (exit 20).
internal sealed class ThrowingDbContextFactory
(
    DbContextOptions<AppDbContext> realOptions,
    int throwOnCall,
    Exception exceptionToThrow
) : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _realOptions = realOptions
        ?? throw new ArgumentNullException(nameof(realOptions));
    private readonly Exception _exceptionToThrow = exceptionToThrow
        ?? throw new ArgumentNullException(nameof(exceptionToThrow));
    private int _callCount;

    public AppDbContext CreateDbContext() => new(_realOptions);

#pragma warning disable VSTHRD200 // Interface implementation
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var current = Interlocked.Increment(ref _callCount);

        return current >= throwOnCall
            ? Task.FromException<AppDbContext>(_exceptionToThrow)
            : Task.FromResult(new AppDbContext(_realOptions));
    }
#pragma warning restore VSTHRD200
}
