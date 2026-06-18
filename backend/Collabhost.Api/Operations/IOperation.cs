namespace Collabhost.Api.Operations;

// The operation spine (code-structure-conventions §8/§9). Every mutating action that both the
// REST endpoint and the MCP tool expose is one findable IOperation<TCommand, TResult>: the
// surface adapts its raw input into TCommand, injects the concrete operation directly (no
// dispatcher, no mediator, no assembly-scan), calls ExecuteAsync, and maps the returned
// OperationResult<TResult> back to its own surface result. The result is the shared core
// written once and zero REST<->MCP duplication.
public interface IOperation<TCommand, TResult>
{
    Task<OperationResult<TResult>> ExecuteAsync(TCommand command, CancellationToken ct);
}
