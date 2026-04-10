namespace Collabhost.Api.Data.AppTypes;

public class TypeStoreSettings
{
    public const string SectionName = "TypeStore";

    public required string UserTypesDirectory { get; init; } = "UserTypes";
}
