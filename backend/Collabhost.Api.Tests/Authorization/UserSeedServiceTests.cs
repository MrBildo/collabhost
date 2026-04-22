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

// Unit tests for the production-startup §11 admin-key 3-scenario model.
public class UserSeedServiceTests : IAsyncLifetime
{
    private string _dataDirectory = null!;
    private TestDbContextFactory _dbFactory = null!;
    private ActivityEventStore _activityEventStore = null!;

    public async Task InitializeAsync()
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

    public Task DisposeAsync()
    {
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

        return Task.CompletedTask;
    }

    private UserSeedService CreateService(string? configuredAdminKey) =>
        CreateService(configuredAdminKey, NullLogger<UserSeedService>.Instance);

    private UserSeedService CreateService(string? configuredAdminKey, ILogger<UserSeedService> logger) =>
        new
        (
            _dbFactory,
            Options.Create(new AuthorizationSettings { AdminKey = configuredAdminKey }),
            _activityEventStore,
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

        // The admin key is surfaced via ILogger.LogCritical so the Console provider emits it
        // on stdout in production without tripping xunit's per-test Console capture. Operators
        // grep for "Collabhost admin key:" (#152 contract, #158 owns UX polish).
        capture.ShouldHaveLogged(LogLevel.Critical, $"Collabhost admin key: {admin.AuthKey}");
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

        capture.ShouldHaveLogged(LogLevel.Critical, "Collabhost admin key:");
    }

    // -- Scenario 2 ------------------------------------------------------

    [Fact]
    public async Task SeedAsync_Scenario2_EmptyDbWithConfiguredKey_InsertsAdminAndSuppressesStdout()
    {
        const string ConfiguredKey = "01SCENARIO2KEY0000000000AA";

        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: ConfiguredKey, capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        users[0].AuthKey.ShouldBe(ConfiguredKey);
        users[0].Role.ShouldBe(UserRole.Administrator);

        // No Critical emission: the operator already has the key (§11.1). Critical is the
        // admin-key-visibility channel; Scenario 2 must not emit.
        capture.ShouldNotHaveLogged(LogLevel.Critical, "Collabhost admin key:");
    }

    // -- Scenario 3 ------------------------------------------------------

    [Fact]
    public async Task SeedAsync_Scenario3_DbHasUsersConfiguredKeyIsNew_InsertsAdditionalAdmin()
    {
        // Seed an initial admin (Scenario 1/2 equivalent) so the DB has users.
        const string ExistingKey = "01EXISTING0ADMIN0KEY000000";
        const string NewKey = "01BREAK0GLASS0KEY0000000AA";

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
        const string ExistingKey = "01EXISTING0ADMIN0KEY000000";
        const string NewKey = "01BREAK0GLASS0KEY0000000AA";

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
        const string MatchingKey = "01MATCHING0ADMIN0KEY000000";

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
        // §11.3 idempotency check is against any user's AuthKey, not just admins. If the
        // configured key happens to collide with an Agent's key, we treat it as a match
        // and do not insert a new admin (the operator would notice the config collision).
        const string MatchingKey = "01AGENT0KEY000000000000000";

        await InsertUserAsync("Existing Admin", "01EXISTING0ADMIN0KEY000000", UserRole.Administrator);
        await InsertUserAsync("Agent", MatchingKey, UserRole.Agent);

        var service = CreateService(configuredAdminKey: MatchingKey);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SeedAsync_Idempotent_NoConfiguredKeyDbHasUsers_NoOp()
    {
        const string ExistingKey = "01EXISTING0ADMIN0KEY000000";

        await InsertUserAsync("Admin", ExistingKey, UserRole.Administrator);

        var capture = new CapturingLogger<UserSeedService>();
        var service = CreateService(configuredAdminKey: null, capture);

        await service.SeedAsync(CancellationToken.None);

        var users = await GetUsersAsync();

        users.Count.ShouldBe(1);
        capture.ShouldNotHaveLogged(LogLevel.Critical, "Collabhost admin key:");
    }

    [Fact]
    public async Task SeedAsync_RestartWithSameConfiguredKey_StableSingleAdmin()
    {
        const string ConfiguredKey = "01STABLE0KEY0000000000000A";

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
}

// Captures ILogger calls so assertions can inspect level + rendered message. File-scoped:
// only UserSeedService tests need to verify the admin-key emission channel.
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

    public void ShouldNotHaveLogged(LogLevel level, string messageSubstring) =>
        _entries.ShouldNotContain
        (
            e => e.Level == level && e.Message.Contains(messageSubstring, StringComparison.Ordinal),
            $"Expected NO log entry at {level} containing '{messageSubstring}' but captured: {string.Join(" | ", _entries)}"
        );
}
