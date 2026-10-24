using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Options;

namespace Collabhost.Api.Authorization;

public class AuthKeyResolver
(
    IOptionsMonitor<AuthorizationSettings> authorizationSettings,
    UserStore userStore,
    ILogger<AuthKeyResolver> logger
)
{
    private readonly IOptionsMonitor<AuthorizationSettings> _authorizationSettings = authorizationSettings
        ?? throw new ArgumentNullException(nameof(authorizationSettings));

    private readonly UserStore _userStore = userStore
        ?? throw new ArgumentNullException(nameof(userStore));

    private readonly ILogger<AuthKeyResolver> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public async Task<User?> ResolveAsync(string authKey, CancellationToken ct)
    {
        var adminKey = _authorizationSettings.CurrentValue.AdminKey;

        // Config key bypass: permanent lockout override -- always works even if DB is empty
        if (adminKey is not null && KeysMatch(authKey, adminKey))
        {
            var user = await _userStore.GetByAuthKeyAsync(authKey, ct);

            if (user is not null)
            {
                return user;
            }

            // DB has no user for the config key (deleted or first request before seed runs).
            // Create a transient admin identity so the request succeeds.
            _logger.LogWarning
            (
                "Auth bypass: request authenticated via config admin key with no matching DB user. "
                + "Create a proper user account."
            );

            return new User
            {
                Name = "Admin (config bypass)",
                AuthKey = authKey,
                Role = UserRole.Administrator,
            };
        }

        return await _userStore.GetByAuthKeyAsync(authKey, ct);
    }

    // Compared in fixed time, so how long a wrong guess takes to reject says nothing about how many of
    // its leading characters were right. Keys of different lengths still return early; the length of
    // a configured key is not the secret.
    internal static bool KeysMatch(string presentedKey, string configuredKey) =>
        CryptographicOperations.FixedTimeEquals
        (
            Encoding.UTF8.GetBytes(presentedKey),
            Encoding.UTF8.GetBytes(configuredKey)
        );
}
