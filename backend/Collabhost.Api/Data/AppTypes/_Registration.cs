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

public class TypeStoreValidationException
(
    IReadOnlyList<TypeStoreValidationError> errors,
    bool isBuiltIn
) : Exception(FormatMessage(errors))
{
    public IReadOnlyList<TypeStoreValidationError> Errors { get; } = errors;

    // True if the validation failure is in a built-in embedded resource (packaging bug).
    // False if it is in an operator-provided user-type JSON file (operator configuration error).
    // Program.cs uses this to choose exit code 30 (packaging bug) vs. 31 (operator error).
    public bool IsBuiltIn { get; } = isBuiltIn;

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
        public IServiceCollection AddTypeStore(TypeStoreSettings settings)
        {
            services.AddSingleton(settings);
            services.AddSingleton<TypeStore>();
            services.AddSingleton<IEventBus<TypeStoreReloadedEvent>, EventBus<TypeStoreReloadedEvent>>();

            return services;
        }
    }

    // Internal visibility for unit tests
    internal static TypeStoreSettings ResolveSettings(IConfiguration configuration)
    {
        // COLLABHOST_USER_TYPES_PATH: env var wins over appsettings, then hardcoded default
        var userTypesPath = Environment.GetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH");

        return !string.IsNullOrWhiteSpace(userTypesPath)
            ? new TypeStoreSettings { UserTypesDirectory = userTypesPath }
            : configuration.GetSection(TypeStoreSettings.SectionName).Get<TypeStoreSettings>()
                ?? new TypeStoreSettings { UserTypesDirectory = "UserTypes" };
    }

    // Resolves the effective user-types directory the TypeStore will scan. Relative paths are
    // composed against AppContext.BaseDirectory to match TypeStore.ResolveUserTypesDirectory.
    // Used by StartupPreflight so preflight and the runtime resolve to the same path.
    public static string ResolveEffectiveUserTypesDirectory(TypeStoreSettings settings) =>
        Path.IsPathRooted(settings.UserTypesDirectory)
            ? settings.UserTypesDirectory
            : Path.Combine(AppContext.BaseDirectory, settings.UserTypesDirectory);
}
