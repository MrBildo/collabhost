using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Authorization;

// Unit tests for the admin-key 3-scenario model (#164 startup contract).
public class UserSeedServiceTests : IAsyncLifetime
{
    private string _dataDirectory = null!;
    private TestDbContextFactory _dbFactory = null!;
    private ActivityEventStore _activityEventStore = null!;

    // What the service printed to standard output, captured per test.
    private readonly StringWriter _standardOutput = new();

    public async ValueTask InitializeAsync()
    {
        _dataDirectory = Path.Combine
        (
            Path.GetTempPath(),
            $"collabhost-userseed-{Guid.NewGuid():N}"
        );

        Directory.CreateDirectory(_dataDirectory);

        var dbPath = Path.Combine(_dataDirectory, "collabhost.db");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
                .Options;

        _dbFactory = new TestDbContextFactory(options);

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            await db.Database.EnsureCreatedAsync();
        }

        _activityEventStore = new ActivityEventStore(_dbFactory, NullLogger<ActivityEventStore>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        await _standardOutput.DisposeAsync();

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
    }

    private string KeyFilePath => Path.Combine(_dataDirectory, FirstRunAdminKeyAnnouncer.KeyFileName);

    private UserSeedService CreateService(string? configuredAdminKey) =>
        CreateService(configuredAdminKey, NullLogger<UserSeedService>.Instance);

    private UserSeedService CreateService
    (
        string? configuredAdminKey,
        ILogger<UserSeedService> logger,
        bool standardOutputIsJournal = false,
        string? dataDirectory = null
    ) =>
        new
        (
            _dbFactory,
            Options.Create(new AuthorizationSettings { AdminKey = configuredAdminKey }),
            _activityEventStore,
            new FirstRunAdminKeyAnnouncer(dataDirectory ?? _dataDirectory, () => _standardOutput, standardOutputIsJournal),
            logger
        );

    private async Task<IReadOnlyList<User>> GetUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        return await db.Users.AsNoTracking().ToListAsync();
    }

    private async Task InsertUserAsync(string name, string authKey, UserRole role)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        db.Users.Add(new User
        {
            Name = name,
            AuthKey = authKey,
            Role = role,
        });

        await db.SaveChangesAsync();
    }

    // -- Scenario 1 ------------------------------------------------------

    [Fact]
    public async Task SeedAsync_Scenario1_EmptyDbNoConfiguredKey_GeneratesAndInsertsAdmin()
    {
        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: null, capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);

        var admin = users[0];
        admin.Name.ShouldBe("Admin");
        admin.Role.ShouldBe(UserRole.Administrator);
        admin.AuthKey.Length.ShouldBe(26); // ULID length

        // The operator gets the key on standard output (the line operators and install checks grep
        // for) and in a file in the data directory. No log record carries it: every logging provider
        // is a sink (the Windows Event Log, journald) that outlives the moment of capture.
        _standardOutput.ToString().ShouldContain($"Collabhost admin key: {admin.AuthKey}", Case.Sensitive);
        (await File.ReadAllTextAsync(KeyFilePath)).Trim().ShouldBe(admin.AuthKey);
        capture.ShouldNotHaveLoggedAtAnyLevel(admin.AuthKey);
        capture.ShouldHaveLogged(LogLevel.Information, KeyFilePath);
    }

    [Fact]
    public async Task SeedAsync_Scenario1_WhitespaceConfiguredKey_TreatedAsUnsetAndGenerates()
    {
        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: "   ", capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        users[0].AuthKey.Length.ShouldBe(26);

        _standardOutput.ToString().ShouldContain($"Collabhost admin key: {users[0].AuthKey}", Case.Sensitive);
        capture.ShouldNotHaveLoggedAtAnyLevel(users[0].AuthKey);
    }

    // Under systemd, standard output is the journal, so printing the key there would keep it in the
    // journal. The key goes to the file only, and the log says where that file is.
    [Fact]
    public async Task SeedAsync_Scenario1_StandardOutputIsJournal_KeyOnlyInFile()
    {
        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: null, capture, standardOutputIsJournal: true);

        await service.SeedAsync(CancellationToken.None);

        var admin = (await GetUsersAsync()).Single();

        _standardOutput.ToString().ShouldBeEmpty();
        (await File.ReadAllTextAsync(KeyFilePath)).Trim().ShouldBe(admin.AuthKey);
        capture.ShouldNotHaveLoggedAtAnyLevel(admin.AuthKey);
        capture.ShouldHaveLogged(LogLevel.Information, KeyFilePath);
    }

    // A key file left by an earlier first run (the database was reset, the file was not) is replaced
    // with the new key, not kept.
    [Fact]
    public async Task SeedAsync_Scenario1_StaleKeyFile_IsReplaced()
    {
        await File.WriteAllTextAsync(KeyFilePath, "01OLDKEY0FROM0EARLIER0RUN0\n");

        var service = CreateService(configuredAdminKey: null);

        await service.SeedAsync(CancellationToken.None);

        var admin = (await GetUsersAsync()).Single();

        (await File.ReadAllTextAsync(KeyFilePath)).Trim().ShouldBe(admin.AuthKey);
    }

    // The key file is the only way a headless install can read the key, so a seed that cannot write
    // it must not commit: the database stays empty and the next boot is a first run again.
    [Fact]
    public async Task SeedAsync_Scenario1_KeyFileCannotBeWritten_ThrowsAndLeavesDbEmpty()
    {
        var missingDirectory = Path.Combine(_dataDirectory, "does-not-exist");

        var service = CreateService
        (
            configuredAdminKey: null,
            NullLogger<UserSeedService>.Instance,
            dataDirectory: missingDirectory
        );

        await Should.ThrowAsync<DirectoryNotFoundException>(() => service.SeedAsync(CancellationToken.None));

        (await GetUsersAsync()).Count.ShouldBe(0);
        _standardOutput.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedAsync_Scenario1_KeyFile_IsReadableByOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes; on Windows the file takes the data directory's ACL");

            return;
        }

        var service = CreateService(configuredAdminKey: null);

        await service.SeedAsync(CancellationToken.None);

        File.GetUnixFileMode(KeyFilePath).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // -- Scenario 2 ------------------------------------------------------

    [Fact]
    public async Task SeedAsync_Scenario2_EmptyDbWithConfiguredKey_InsertsAdminAndSuppressesStdout()
    {
        const string ConfiguredKey = "01KW5KB5AMWMRC8KN5JKZ9BYCV";

        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: ConfiguredKey, capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ConfiguredKey);
        users[0].Role.ShouldBe(UserRole.Administrator);

        // Nothing handed out: the operator already has the key (Scenario 2 -- configured first run).
        _standardOutput.ToString().ShouldBeEmpty();
        File.Exists(KeyFilePath).ShouldBeFalse();
        capture.ShouldNotHaveLoggedAtAnyLevel(ConfiguredKey);
    }

    // -- Scenario 3 ------------------------------------------------------

    [Fact]
    public async Task SeedAsync_Scenario3_DbHasUsersConfiguredKeyIsNew_InsertsAdditionalAdmin()
    {
        // Seed an initial admin (Scenario 1/2 equivalent) so the DB has users.
        const string ExistingKey = "01KW5KB5APT8PK1FS19DVCY2Q7";
        const string NewKey = "01KW5KB5AP4FC4EDM2YE5GGPTC";

        await InsertUserAsync("Admin", ExistingKey, UserRole.Administrator);

        var service = CreateService(configuredAdminKey: NewKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        // Break-glass additive: existing admin remains, new admin inserted alongside
        users.Count.ShouldBe(2);
        users.ShouldContain(u => u.AuthKey == ExistingKey && u.Name == "Admin");
        users.ShouldContain(u => u.AuthKey == NewKey && u.Role == UserRole.Administrator);
    }

    [Fact]
    public async Task SeedAsync_Scenario3_PreservesExistingAdminRoleAndName()
    {
        const string ExistingKey = "01KW5KB5APT8PK1FS19DVCY2Q7";
        const string NewKey = "01KW5KB5AP4FC4EDM2YE5GGPTC";

        await InsertUserAsync("Custom Admin Name", ExistingKey, UserRole.Administrator);

        var service = CreateService(configuredAdminKey: NewKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        // Existing admin is not mutated -- Scenario 3 is additive, not replacing
        var existing = users.Single(u => u.AuthKey == ExistingKey);
        existing.Name.ShouldBe("Custom Admin Name");
        existing.Role.ShouldBe(UserRole.Administrator);
    }

    // -- Idempotency -----------------------------------------------------

    [Fact]
    public async Task SeedAsync_Idempotent_ConfiguredKeyMatchesExistingUser_NoOp()
    {
        const string MatchingKey = "01KW5KB5AP9NCV4A890FTXEXCP";

        await InsertUserAsync("Admin", MatchingKey, UserRole.Administrator);

        var service = CreateService(configuredAdminKey: MatchingKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        // No new row inserted -- matches existing
        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(MatchingKey);
    }

    [Fact]
    public async Task SeedAsync_Idempotent_ConfiguredKeyMatchesNonAdminUser_NoOp()
    {
        // Idempotency check is against any user's AuthKey, not just admins. If the configured
        // key happens to collide with an Agent's key, we treat it as a match and do not
        // insert a new admin (the operator would notice the config collision).
        const string MatchingKey = "01KW5KB5AP98ZCEYX8DKAJZ7E8";

        await InsertUserAsync("Existing Admin", "01KW5KB5APT8PK1FS19DVCY2Q7", UserRole.Administrator);
        await InsertUserAsync("Agent", MatchingKey, UserRole.Agent);

        var service = CreateService(configuredAdminKey: MatchingKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SeedAsync_Idempotent_NoConfiguredKeyDbHasUsers_NoOp()
    {
        const string ExistingKey = "01KW5KB5APT8PK1FS19DVCY2Q7";

        await InsertUserAsync("Admin", ExistingKey, UserRole.Administrator);

        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: null, capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        _standardOutput.ToString().ShouldBeEmpty();
        File.Exists(KeyFilePath).ShouldBeFalse();
        capture.ShouldNotHaveLoggedAtAnyLevel(ExistingKey);
    }

    [Fact]
    public async Task SeedAsync_RestartWithSameConfiguredKey_StableSingleAdmin()
    {
        const string ConfiguredKey = "01KW5KB5APHF940SV6DWJS9Y52";

        var service = CreateService(configuredAdminKey: ConfiguredKey);

        // Boot 1 -- Scenario 2
        await service.SeedAsync(CancellationToken.None);

        // Boot 2 -- same key, idempotent no-op
        await service.SeedAsync(CancellationToken.None);

        // Boot 3 -- same key, idempotent no-op
        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ConfiguredKey);
    }

    // -- Configured-key shape validation (#168 MED) ----------------------

    [Fact]
    public async Task SeedAsync_ConfiguredKeyExceedsMaxLength_EmptyDb_ThrowsAndLeavesDbEmpty()
    {
        // One character over the AuthKey schema length. SQLite would silently persist it (MaxLength
        // is not enforced at the DB layer), so the seed path must fail loud instead of accepting a
        // schema-violating key.
        var overLongKey = new string('A', User.AuthKeyMaxLength + 1);

        var service = CreateService(configuredAdminKey: overLongKey);

        var ex = await Should.ThrowAsync<AdminKeyConfigurationException>
        (
            () => service.SeedAsync(CancellationToken.None)
        );

        ex.ConfiguredKeyLength.ShouldBe(User.AuthKeyMaxLength + 1);
        ex.MaxKeyLength.ShouldBe(User.AuthKeyMaxLength);

        var users = await GetUsersAsync();

        // Validation runs before any DB write -- no partial seed.
        users.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SeedAsync_ConfiguredKeyExceedsMaxLength_DbHasUsers_ThrowsBeforeAdditionalInsert()
    {
        const string ExistingKey = "01KW5KB5APT8PK1FS19DVCY2Q7";

        await InsertUserAsync("Admin", ExistingKey, UserRole.Administrator);

        var overLongKey = new string('B', User.AuthKeyMaxLength + 5);

        var service = CreateService(configuredAdminKey: overLongKey);

        await Should.ThrowAsync<AdminKeyConfigurationException>
        (
            () => service.SeedAsync(CancellationToken.None)
        );

        var users = await GetUsersAsync();

        // Scenario 3 (break-glass) validation gates the additive insert -- existing admin
        // untouched, no new row from the rejected key.
        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ExistingKey);
    }

    [Fact]
    public async Task SeedAsync_ConfiguredKeyShorterThanMax_NonUlid_SeedsWithoutRejection()
    {
        // Length-only validation against the schema: a short custom key is still functional
        // (AuthKeyResolver compares by string equality), so it must NOT be rejected even though it
        // is not a 26-char ULID. Guards against over-tightening to a full ULID-parse check, which
        // would break existing installs that configured a short custom key.
        const string ShortKey = "shortkey99";

        var service = CreateService(configuredAdminKey: ShortKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ShortKey);
    }

    // -- Atomicity (transaction rollback on SIGINT) -----------------------

    [Fact]
    public async Task SeedAsync_Scenario1_CancelledBeforeCommit_LeavesDbEmpty()
    {
        // A pre-cancelled token simulates a SIGINT arriving during the transaction window.
        // The seeder must propagate the cancellation to the transaction so the DB is
        // left in the pre-seed state -- no partial rows committed.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = CreateService(configuredAdminKey: null);

        await Should.ThrowAsync<OperationCanceledException>
        (
            () => service.SeedAsync(cts.Token)
        );

        var users = await GetUsersAsync();

        users.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SeedAsync_Scenario3_CancelledBeforeCommit_LeavesDbUnchanged()
    {
        // Pre-existing admin -- Scenario 3 (break-glass insert) path.
        // Cancellation during the transaction must not commit the new admin row.
        const string ExistingKey = "01KW5KB5APT8PK1FS19DVCY2Q7";
        const string NewKey = "01KW5KB5AP4FC4EDM2YE5GGPTC";

        await InsertUserAsync("Admin", ExistingKey, UserRole.Administrator);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var service = CreateService(configuredAdminKey: NewKey);

        await Should.ThrowAsync<OperationCanceledException>
        (
            () => service.SeedAsync(cts.Token)
        );

        var users = await GetUsersAsync();

        // Only the pre-existing admin -- no new row from the cancelled seed attempt
        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ExistingKey);
    }
}

// Captures ILogger calls so assertions can inspect level + rendered message. File-scoped:
// only UserSeedService tests need to verify what the seed logs -- and that the admin key isn't in it.
file sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

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
        ArgumentNullException.ThrowIfNull(formatter);

        _entries.Add((logLevel, formatter(state, exception)));
    }

    public void ShouldHaveLogged(LogLevel level, string messageSubstring) =>
        _entries.ShouldContain
        (
            e => e.Level == level && e.Message.Contains(messageSubstring, StringComparison.Ordinal),
            $"Expected a log entry at {level} containing '{messageSubstring}' but captured: {string.Join(" | ", _entries)}"
        );

    public void ShouldNotHaveLoggedAtAnyLevel(string messageSubstring) =>
        _entries.ShouldNotContain
        (
            e => e.Message.Contains(messageSubstring, StringComparison.Ordinal),
            $"Expected NO log entry containing '{messageSubstring}' but captured: {string.Join(" | ", _entries)}"
        );
}
