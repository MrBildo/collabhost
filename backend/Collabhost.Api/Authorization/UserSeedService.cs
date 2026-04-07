using System.Globalization;

using Collabhost.Api.Data;

using Microsoft.Extensions.Options;

namespace Collabhost.Api.Authorization;

public class UserSeedService
(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<AuthorizationSettings> authorizationSettings,
    ILogger<UserSeedService> logger
) : IHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory = dbFactory
        ?? throw new ArgumentNullException(nameof(dbFactory));

    private readonly AuthorizationSettings _authorizationSettings = authorizationSettings.Value;

    private readonly ILogger<UserSeedService> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var hasUsers = await db.Users.AnyAsync(cancellationToken);

        if (hasUsers)
        {
            return;
        }

        var adminKey = _authorizationSettings.AdminKey
            ?? Ulid.NewUlid().ToString(null, CultureInfo.InvariantCulture);

        var admin = new User
        {
            Name = "Admin",
            AuthKey = adminKey,
            Role = UserRole.Administrator,
        };

        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        var keyHint = adminKey[..Math.Min(8, adminKey.Length)] + "...";

        _logger.LogInformation("Admin user seeded. Key: {AdminKeyHint}", keyHint);

        // Full key written to stdout for operator visibility -- not captured in structured log exports
        Console.WriteLine($"[Collabhost] Admin key: {adminKey}");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
