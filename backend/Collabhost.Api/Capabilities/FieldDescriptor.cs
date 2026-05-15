namespace Collabhost.Api.Capabilities;

public record FieldDescriptor
(
    string Key,
    string Label,
    FieldType Type,
    FieldEditable Editable,
    bool Required = false,
    bool RequiresRestart = false,
    string? HelpText = null,
    string? Unit = null,
    IReadOnlyList<FieldOption>? Options = null,
    FieldDependency? DependsOn = null,
    // KeyValue-only. The regex a key must satisfy plus the operator-facing
    // message when it does not. Absent => consumers (server validation and the
    // frontend KeyValueField) fall back to the environment-variable key
    // contract. This is the frontend mirror of a server-authoritative rule,
    // not frontend-only trust: CapabilityResolver.ValidateEdits enforces the
    // same pattern server-side. Card #308.
    string? KeyPattern = null,
    string? KeyPatternMessage = null
);

public record FieldOption(string Value, string Label);

public record FieldDependency(string Field, string Value);

public enum FieldType
{
    Text,
    Number,
    Boolean,
    Select,
    Directory,
    KeyValue
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(FieldEditableAlways), "always")]
[JsonDerivedType(typeof(FieldEditableLocked), "locked")]
[JsonDerivedType(typeof(FieldEditableDerived), "derived")]
public abstract record FieldEditable;

public record FieldEditableAlways : FieldEditable;

public record FieldEditableLocked(string Reason) : FieldEditable;

public record FieldEditableDerived(string Reason) : FieldEditable;
