namespace Collabhost.Api.Data.AppTypes;

public record TypeStoreReloadedEvent
(
    int BuiltInCount,
    int UserCount,
    int BindingCount
);
