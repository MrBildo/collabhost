using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Data;

using Microsoft.Extensions.Options;

namespace Collabhost.Api.Authorization;

// Admin-user seeding. Implements the 3-scenario model from production-startup.md §11.
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
// Invoked from Program.cs as phase (8) of the startup sequence (§4). Failure halts startup
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

        // Normalize: whitespace-only configured values are treated as unset. Mirrors the
        // Phase 3 env-var readers (§11.5 idiom) so a blank COLLABHOST_ADMIN_KEY in a
        // startup wrapper is equivalent to no override at all.
        var configuredKey = string.IsNullOrWhiteSpace(_authorizationSettings.AdminKey)
            ? null
            : _authorizationSettings.AdminKey;

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
            : (Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture), true);

        var admin = new User
        {
            Name = "Admin",
            AuthKey = adminKey,
            Role = UserRole.Administrator,
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        await RecordSeedActivityAsync("first-run");

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

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        await RecordSeedActivityAsync("recovery-override");

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
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.UserSeeded,
                    ActorId = ActivityActor.SystemId,
                    ActorName = ActivityActor.SystemName,
                    MetadataJson = JsonSerializer.Serialize
                    (
                        new { role = "administrator", seedKind }
                    )
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for admin user seed");
        }
    }
}
