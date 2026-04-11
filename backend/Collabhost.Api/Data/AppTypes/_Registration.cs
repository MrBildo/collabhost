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
            var settings = configuration.GetSection(TypeStoreSettings.SectionName).Get<TypeStoreSettings>()
                ?? new TypeStoreSettings { UserTypesDirectory = "UserTypes" };

            services.AddSingleton(settings);
            services.AddSingleton<TypeStore>();
            services.AddSingleton<IEventBus<TypeStoreReloadedEvent>, EventBus<TypeStoreReloadedEvent>>();

            return services;
        }
    }
}
