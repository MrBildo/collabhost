namespace Collabhost.Api.Common;

public record CommandResult(bool IsSuccess, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CommandResult Success() => new(true);

    public static CommandResult Fail(string? errorCode = null, string? errorMessage = null) =>
        new(false, errorCode, errorMessage);
}

public record CommandResult<T>(bool IsSuccess, T? Value = default, string? ErrorCode = null, string? ErrorMessage = null)
{
    public static CommandResult<T> Success(T value) => new(true, value);

    public static CommandResult<T> Fail(string? errorCode = null, string? errorMessage = null) =>
        new(false, default, errorCode, errorMessage);
}
