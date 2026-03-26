namespace Collabhost.Api.Common;

public record QueryResult<T>(bool IsSuccess, T? Value = default, string? ErrorMessage = null)
{
    public static QueryResult<T> Success(T value) => new(true, value);

    public static QueryResult<T> Fail(string? errorMessage = null) =>
        new(false, default, errorMessage);
}
