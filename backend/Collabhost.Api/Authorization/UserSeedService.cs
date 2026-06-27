using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data;
using Collabhost.Api.Shared;

using Microsoft.Extensions.Options;

namespace Collabhost.Api.Authorization;

// Admin-user seeding. Implements the 3-scenario model (#164 startup contract):
//
//   Scenario 1 -- Blind first run: empty DB, no configured key. Generate a ULID, insert an
//                 Admin user with that key, write the key to stdout so the operator can
//                 capture it (#158 owns the UX; this stays behaviour-stable).
//   Scenario 2 -- Configured first run: empty DB, configured key present. Insert an Admin
//                 user with the configured key. No stdout (operator already has it).
//   Scenario 3 -- Override on subsequent boot: DB has users, configured key present and
//                 does not match any existing user's AuthKey. Insert a NEW Admin user with
//                 the configured key (break-glass additive behavior). Existing admins
//                 remain. If the configured key matches an existing user: no-op.
//
// Invoked from Program.cs as phase (8) of the startup sequence. Failure halts startup
// with exit code 40 via StartupStderr.
public class UserSeedService
(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<AuthorizationSettings> authorizationSettings,
    ActivityEventStore activityEventStore,
    ILogger<UserSeedService> logger
)
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly AuthorizationSettings _authorizationSettings = authorizationSettings.Value;

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<UserSeedService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        // Normalize: whitespace-only configured values are treated as unset. A blank
        // COLLABHOST_ADMIN_KEY in a startup wrapper is equivalent to no override at all.
        var configuredKey = string.IsNullOrWhiteSpace(_authorizationSettings.AdminKey)
            ? null
            : _authorizationSettings.AdminKey;

        // Fail-loud-on-misconfiguration (Standard Hosting model #4): a configured key longer than
        // the AuthKey schema allows would persist as-is (SQLite ignores MaxLength), silently
        // violating the declared contract. Reject it at seed time -- before any DB write so neither
        // Scenario 2 nor Scenario 3 inserts a partial/invalid row -- and let Program.cs halt startup
        // with the canonical stderr block. Length-only against the schema: a shorter custom key is
        // still functional (AuthKeyResolver compares by string equality), so over-rejecting it would
        // break existing installs.
        if (configuredKey is not null && configuredKey.Length > User.AuthKeyMaxLength)
        {
            throw new AdminKeyConfigurationException(configuredKey.Length, User.AuthKeyMaxLength);
        }

        var hasUsers = await db.Users.AnyAsync(cancellationToken);

        if (!hasUsers)
        {
            await SeedFirstRunAsync(db, configuredKey, cancellationToken);
            return;
        }

        if (configuredKey is null)
        {
            // Subsequent boot, no configured key. Nothing to reconcile.
            return;
        }

        var keyAlreadyExists = await db.Users
            .AnyAsync(u => u.AuthKey == configuredKey, cancellationToken);

        if (keyAlreadyExists)
        {
            // Idempotent: configured key matches an existing user -- no-op.
            return;
        }

        await InsertAdditionalAdminAsync(db, configuredKey, cancellationToken);
    }

    private async Task SeedFirstRunAsync
    (
        AppDbContext db,
        string? configuredKey,
        CancellationToken cancellationToken
    )
    {
        var (adminKey, wasGenerated) = configuredKey is not null
            ? (configuredKey, false)
            : (Ulid.NewUlid().ToCanonicalString(), true);

        var admin = new User
        {
            Name = "Admin",
            AuthKey = adminKey,
            Role = UserRole.Administrator,
        };

        // Wrap the insert in a transaction so a first-boot SIGINT cannot leave a
        // partial state -- the DB is either fully seeded or untouched after a crash.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        await RecordSeedActivityAsync(SeedKinds.FirstRun);

        if (wasGenerated)
        {
            var keyHint = adminKey[..Math.Min(8, adminKey.Length)] + "...";

            _logger.LogInformation("Admin user seeded. Key: {AdminKeyHint}", keyHint);

            // Full key surfaced at Critical so operators see it on stdout via the Console
            // provider -- the Scenario 1 "blind first run" UX contract from #152 / #156.3.
            // ILogger.LogCritical (not Console.WriteLine) because xunit captures Console.Out
            // per-test via a StringWriter that gets disposed between tests; a Console.Write
            // during subsequent WebApplicationFactory startup hits a disposed writer. #158
            // owns the UX polish (format, recovery-if-missed). Operator-grepable marker:
            // "Collabhost admin key:".
            _logger.LogCritical("Collabhost admin key: {AdminKey}", adminKey);
        }
        else
        {
            _logger.LogInformation("Admin user seeded with configured admin key");
        }
    }

    private async Task InsertAdditionalAdminAsync
    (
        AppDbContext db,
        string configuredKey,
        CancellationToken cancellationToken
    )
    {
        var admin = new User
        {
            Name = "Admin (recovery)",
            AuthKey = configuredKey,
            Role = UserRole.Administrator,
        };

        // Wrap the insert in a transaction so a first-boot SIGINT cannot leave a
        // partial state -- the DB is either fully seeded or untouched after a crash.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        await RecordSeedActivityAsync(SeedKinds.RecoveryOverride);

        _logger.LogInformation
        (
            "Configured admin key is new -- created additional Admin user for recovery"
        );
    }

    private async Task RecordSeedActivityAsync(string seedKind)
    {
        try
        {
            await _activityEventStore.RecordAsync
            (
                ActivityEvent.ForSystem
                (
                    ActivityEventTypes.UserSeeded,
                    metadataJson: JsonSerializer.Serialize(new { role = "administrator", seedKind })
                ),
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for admin user seed");
        }
    }
}

// Seed-kind discriminators recorded in the UserSeeded activity event's metadata. Const strings
// (not an enum) on purpose: the values are serialized verbatim into the persisted MetadataJson, so
// promoting to an enum would change the on-disk shape ("first-run" -> 0 or "FirstRun"). Const class
// gives refactor safety while preserving the exact persisted values.
file static class SeedKinds
{
    public const string FirstRun = "first-run";

    public const string RecoveryOverride = "recovery-override";
}

// Thrown by UserSeedService.SeedAsync when a configured admin key fails shape validation. Program.cs
// phase 8 maps this to the canonical startup-failure stderr block and halts with exit 40 (the
// seeding-phase halt code). Fail-loud-on-misconfiguration: a malformed configured key halts the boot
// rather than silently persisting a schema-violating value.
public sealed class AdminKeyConfigurationException(int configuredKeyLength, int maxKeyLength)
    : Exception("configured admin key is invalid")
{
    public int ConfiguredKeyLength { get; } = configuredKeyLength;

    public int MaxKeyLength { get; } = maxKeyLength;
}
