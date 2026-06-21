using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Collabhost.Api.Data;

public sealed class MigrationRunner
(
    IDbContextFactory<AppDbContext> dbContextFactory,
    ILogger<MigrationRunner> logger
)
{
    public const int BackupRetentionCount = 5;
    public const string BackupFilePrefix = "collabhost.db.bak-";

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory = dbContextFactory
        ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ILogger<MigrationRunner> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken ct)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(ct);

        var pending = await context.Database.GetPendingMigrationsAsync(ct);

        return [.. pending];
    }

    public async Task<MigrationOutcome> MigrateWithBackupAsync
    (
        string dataDirectory,
        string backupsDirectory,
        string fromSemver,
        string toSemver,
        CancellationToken ct
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromSemver);
        ArgumentException.ThrowIfNullOrWhiteSpace(toSemver);

        IReadOnlyList<string> pending;

        try
        {
            pending = await GetPendingMigrationsAsync(ct);
        }
        catch (SqliteException ex) when (IsLockedException(ex))
        {
            throw new MigrationFailedException
            (
                11,
                "database is locked",
                null,
                null,
                ex,
                [
                    "Another Collabhost process may be using the database. Stop it first.",
                    $"Database location: {ResolveDbPath(dataDirectory)}",
                    "Do not retry immediately; verify no other instance is running."
                ]
            );
        }

        if (pending.Count == 0)
        {
            _logger.LogInformation("No pending migrations; schema is current.");
            return new MigrationOutcome(false, null, []);
        }

        _logger.LogInformation
        (
            "Pending migrations detected: {Count} ({Migrations})",
            pending.Count,
            string.Join(", ", pending)
        );

        string? backupPath = null;

        var dbPath = ResolveDbPath(dataDirectory);

        // Take a backup only if an existing DB file is on disk. On first run the DB does not yet
        // exist and there is nothing to back up; EF Core will create and migrate it atomically.
        if (File.Exists(dbPath))
        {
            backupPath = BuildBackupPath(backupsDirectory, fromSemver, toSemver);

            // A plain File.Copy of the main .db is unsafe under WAL journal mode (enabled at
            // registration): recently committed transactions live in the `-wal` sidecar until a
            // checkpoint folds them back, so copying the main file alone can capture a stale DB.
            // VACUUM INTO produces a single consistent snapshot that reads through the current WAL
            // state -- the backup is a self-contained file with no sidecars, restorable by copy-back.
            // A pre-existing target is a filename collision (timestamp clash) -- refuse, exit 11.
            if (File.Exists(backupPath))
            {
                throw new MigrationFailedException
                (
                    11,
                    "backup filename collision; refusing to proceed",
                    backupPath,
                    pending[0],
                    null,
                    [
                        "A backup with this timestamp already exists. Wait a moment and retry.",
                        "If this persists, inspect the backups directory for stuck files."
                    ]
                );
            }

            try
            {
                await CreateConsistentBackupAsync(dbPath, backupPath, ct);
            }
            catch (SqliteException ex)
            {
                throw new MigrationFailedException
                (
                    20,
                    "pre-migration backup failed",
                    null,
                    pending[0],
                    ex,
                    [
                        "Verify filesystem permissions on the data directory.",
                        "Ensure sufficient disk space before retrying."
                    ]
                );
            }
            catch (IOException ex)
            {
                throw new MigrationFailedException
                (
                    20,
                    "pre-migration backup failed",
                    null,
                    pending[0],
                    ex,
                    [
                        "Verify filesystem permissions on the data directory.",
                        "Ensure sufficient disk space before retrying."
                    ]
                );
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new MigrationFailedException
                (
                    20,
                    "pre-migration backup failed",
                    null,
                    pending[0],
                    ex,
                    ["Verify filesystem permissions on the data directory."]
                );
            }

            _logger.LogInformation
            (
                "Pre-migration backup created at {BackupPath} (from={FromSemver}, to={ToSemver})",
                backupPath,
                fromSemver,
                toSemver
            );

            TryPruneBackups(backupsDirectory, _logger);
        }
        else
        {
            _logger.LogInformation
            (
                "No existing database at {DbPath}; first-run migrations will create it.",
                dbPath
            );
        }

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(ct);
            await context.Database.MigrateAsync(ct);
        }
        catch (SqliteException ex) when (IsLockedException(ex))
        {
            throw new MigrationFailedException
            (
                11,
                "database is locked",
                backupPath,
                pending[0],
                ex,
                [
                    "Another Collabhost process may be using the database. Stop it first.",
                    $"Database location: {dbPath}"
                ]
            );
        }
        catch (Exception ex)
        {
            throw new MigrationFailedException
            (
                20,
                "migration failed",
                backupPath,
                pending[0],
                ex,
                [
                    "See INSTALL.md -> Troubleshooting -> \"If an upgrade fails.\"",
                    backupPath is not null
                        ? $"Restore the backup at {backupPath}."
                        : "No backup was taken (first-run migration); remove the data directory to retry from scratch.",
                    "Re-install the previous Collabhost version."
                ]
            );
        }

        _logger.LogInformation
        (
            "Migrations applied successfully: {Migrations}",
            string.Join(", ", pending)
        );

        return new MigrationOutcome(true, backupPath, pending);
    }

    // VACUUM INTO writes a fresh, fully-consistent single-file copy of the live database to the
    // target path, reading through the current WAL state -- so commits still resident in the `-wal`
    // sidecar are captured (a plain File.Copy of the main file misses them). The target must not
    // exist; the caller pre-checks for collision. The source is the same file the runner already
    // gates the backup on (ResolveDbPath) -- the EF context's data source is anchored to this path.
    private static async Task CreateConsistentBackupAsync(string dbPath, string backupPath, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");

        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();

        command.CommandText = "VACUUM main INTO $target;";
        command.Parameters.AddWithValue("$target", backupPath);

        await command.ExecuteNonQueryAsync(ct);
    }

    public static string ResolveDbPath(string dataDirectory) =>
        Path.Combine(dataDirectory, "collabhost.db");

    internal static string BuildBackupPath
    (
        string backupsDirectory,
        string fromSemver,
        string toSemver
    )
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var filename = $"{BackupFilePrefix}{timestamp}-pre-{fromSemver}-to-{toSemver}";

        return Path.Combine(backupsDirectory, filename);
    }

    internal static void TryPruneBackups(string backupsDirectory, ILogger logger)
    {
        try
        {
            var files = Directory
                .EnumerateFiles(backupsDirectory, BackupFilePrefix + "*")
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(info => info.CreationTimeUtc)
                        .ToArray();

            if (files.Length <= BackupRetentionCount)
            {
                return;
            }

            foreach (var stale in files.Skip(BackupRetentionCount))
            {
                try
                {
                    stale.Delete();

                    logger.LogInformation
                    (
                        "Pruned old pre-migration backup: {BackupPath}",
                        stale.FullName
                    );
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex, "Failed to prune old backup at {BackupPath}", stale.FullName);
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Failed to prune old backup at {BackupPath}", stale.FullName);
                }
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate backups at {BackupsDirectory}", backupsDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate backups at {BackupsDirectory}", backupsDirectory);
        }
    }

    private static bool IsLockedException(SqliteException ex) =>
        ex.SqliteErrorCode == 5  // SQLITE_BUSY
        || ex.SqliteErrorCode == 6 // SQLITE_LOCKED
        || (ex.Message?.Contains("locked", StringComparison.OrdinalIgnoreCase) ?? false);
}

public sealed record MigrationOutcome
(
    bool Migrated,
    string? BackupPath,
    IReadOnlyList<string> AppliedMigrations
);

// Thrown by MigrationRunner when startup must halt. Program.cs catches this and routes through
// StartupStderr.Write + Environment.Exit. Keeping the exception as the signalling channel means
// the runner itself has no coupling to Console.Error or process termination.
public sealed class MigrationFailedException : Exception
{
    public MigrationFailedException
    (
        int exitCode,
        string summary,
        string? backupPath,
        string? migrationAttempted,
        Exception? cause,
        IReadOnlyList<string> recoverySteps
    )
        : base(summary, cause)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(exitCode, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(recoverySteps);

        ExitCode = exitCode;
        Summary = summary;
        BackupPath = backupPath;
        MigrationAttempted = migrationAttempted;
        RecoverySteps = recoverySteps;
    }

    public int ExitCode { get; }

    public string Summary { get; }

    public string? BackupPath { get; }

    public string? MigrationAttempted { get; }

    public IReadOnlyList<string> RecoverySteps { get; }
}
