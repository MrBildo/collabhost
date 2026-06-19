namespace Collabhost.Api.Operations;

// The abstract failure kind an operation returns. It is surface-agnostic on purpose: the
// REST<->MCP error-shape divergence is surface MAPPING, not operation logic. Each surface
// translates the kind to its own shape -- REST maps Validation->400, NotFound->404,
// Conflict->409 (the exact statuses today's handlers return); MCP maps Validation and Conflict
// ->InvalidParameters, NotFound->AppNotFound. This enum is the only new vocabulary the spine
// introduces, and it is forced by that genuine error-shape divergence.
public enum OperationFailureKind
{
    NotFound,
    Validation,
    Conflict
}

// The normalized outcome of an operation -- success-with-value or a typed failure. A *Result
// record per the closed-suffix vocabulary (§2). Sealed because it is a value-semantics record
// (idiomatic per the dotnet-dev seal carve-out, no justification comment needed).
public sealed record OperationResult<T>
{
    public bool IsSuccess { get; private init; }

    public T? Value { get; private init; }

    public OperationFailureKind FailureKind { get; private init; }

    public string? Error { get; private init; }

    public static OperationResult<T> Success(T value) =>
        new() { IsSuccess = true, Value = value };

    public static OperationResult<T> NotFound(string error) =>
        new() { FailureKind = OperationFailureKind.NotFound, Error = error };

    public static OperationResult<T> Validation(string error) =>
        new() { FailureKind = OperationFailureKind.Validation, Error = error };

    public static OperationResult<T> Conflict(string error) =>
        new() { FailureKind = OperationFailureKind.Conflict, Error = error };
}
