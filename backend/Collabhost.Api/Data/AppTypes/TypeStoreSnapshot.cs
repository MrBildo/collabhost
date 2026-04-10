namespace Collabhost.Api.Data.AppTypes;

public record TypeStoreSnapshot
(
    IReadOnlyList<AppTypeDefinition> Types,
    FrozenDictionary<string, AppTypeDefinition> TypesBySlug,
    FrozenDictionary<string, IReadOnlyDictionary<string, string>> BindingsByTypeSlug
);
