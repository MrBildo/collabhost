using Collabhost.Api.Events;

namespace Collabhost.Api.Data.AppTypes;

public class TypeStoreSettings
{
    public const string SectionName = "TypeStore";

    public required string UserTypesDirectory { get; init; } = "UserTypes";
}

public record TypeStoreReloadedEvent
(
    int BuiltInCount,
    int UserCount,
    int BindingCount
);

public record TypeStoreSnapshot
(
    IReadOnlyList<AppType> Types,
    FrozenDictionary<string, AppType> TypesBySlug,
    FrozenDictionary<string, IReadOnlyDictionary<string, string>> BindingsByTypeSlug
);

public record TypeStoreValidationError(string Source, string FieldPath, string Message);

public class TypeStoreValidationException(IReadOnlyList<TypeStoreValidationError> errors)
    : Exception(FormatMessage(errors))
{
    public IReadOnlyList<TypeStoreValidationError> Errors { get; } = errors;

    private static string FormatMessage(IReadOnlyList<TypeStoreValidationError> errors)
    {
        var lines = errors
            .Select(error => $"  {error.Source}: {error.FieldPath} -- {error.Message}");

        return $"TypeStore validation errors:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }
}

public static class TypeStoreRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddTypeStore(IConfiguration configuration)
        {
            var settings = ResolveSettings(configuration);

            services.AddSingleton(settings);
            services.AddSingleton<TypeStore>();
            services.AddSingleton<IEventBus<TypeStoreReloadedEvent>, EventBus<TypeStoreReloadedEvent>>();

            return services;
        }
    }

    // Internal visibility for unit tests
    internal static TypeStoreSettings ResolveSettings(IConfiguration configuration)
    {
        // COLLABHOST_USER_TYPES_PATH: env var wins over appsettings, then hardcoded default (§12.3 precedence)
        var userTypesPath = Environment.GetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH");

        return !string.IsNullOrWhiteSpace(userTypesPath)
            ? new TypeStoreSettings { UserTypesDirectory = userTypesPath }
            : configuration.GetSection(TypeStoreSettings.SectionName).Get<TypeStoreSettings>()
                ?? new TypeStoreSettings { UserTypesDirectory = "UserTypes" };
    }
}
