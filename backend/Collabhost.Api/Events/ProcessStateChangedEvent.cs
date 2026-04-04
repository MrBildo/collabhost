using Collabhost.Api.Registry;

namespace Collabhost.Api.Events;

public record ProcessStateChangedEvent
(
    Ulid AppId,
    string AppSlug,
    ProcessState PreviousState,
    ProcessState NewState,
    int? Port
);
