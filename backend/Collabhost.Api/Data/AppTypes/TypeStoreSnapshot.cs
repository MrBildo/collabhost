namespace Collabhost.Api.Data.AppTypes;

public record TypeStoreSnapshot
(
    IReadOnlyList<AppType> Types,
    FrozenDictionary<string, AppType> TypesBySlug,
    FrozenDictionary<string, IReadOnlyDictionary<string, string>> BindingsByTypeSlug
);
