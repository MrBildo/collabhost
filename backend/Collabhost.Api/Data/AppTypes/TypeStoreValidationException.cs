namespace Collabhost.Api.Data.AppTypes;

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

public record TypeStoreValidationError(string Source, string FieldPath, string Message);
