using Collabhost.Api.Capabilities;

namespace Collabhost.Api.Registry;

// JSON-serialized DTOs -- List<T> is practical for response types
#pragma warning disable MA0016
// --- App Type List ---

public record AppTypeListItem
(
    string Id,
    string Name,
    string DisplayName,
    string? Description,
    List<AppTag> Tags,
    bool IsBuiltIn
);

// --- Registration Schema ---

public record RegistrationSchema
(
    RegistrationAppType AppType,
    List<AppTag> Tags,
    List<RegistrationSection> Sections
);

public record RegistrationAppType
(
    string Id,
    string Name,
    string DisplayName,
    string? Description
);

public record RegistrationSection
(
    string Key,
    string Title,
    List<RegistrationField> Fields
);

public record RegistrationField
(
    string Key,
    string Label,
    string Type,
    bool Required,
    object? DefaultValue,
    string? Placeholder = null,
    string? HelpText = null,
    List<FieldOption>? Options = null
);
