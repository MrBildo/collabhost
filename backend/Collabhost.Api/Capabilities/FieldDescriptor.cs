namespace Collabhost.Api.Capabilities;

public record FieldDescriptor
(
    string Key,
    string Label,
    FieldType Type,
    FieldEditable Editable,
    bool Required = false,
    string? HelpText = null,
    string? Unit = null,
    IReadOnlyList<FieldOption>? Options = null,
    FieldDependency? DependsOn = null
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
