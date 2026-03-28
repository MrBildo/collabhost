namespace Collabhost.Api.Common;

#pragma warning disable S2326 // Phantom type parameter used for handler constraint resolution
public interface ICommand<TResult>;
#pragma warning restore S2326

public interface ICommandHandler<TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<CommandResult<TResult>> HandleAsync(TCommand command, CancellationToken ct = default);
}

public readonly struct Empty
{
    public static readonly Empty Value;
}

public sealed class CommandDispatcher(IServiceProvider serviceProvider)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public async Task<CommandResult<TResult>> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<TResult>
    {
        var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        return await handler.HandleAsync(command, ct);
    }

    public async Task<CommandResult<Empty>> DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default)
        where TCommand : ICommand<Empty>
    {
        return await DispatchAsync<TCommand, Empty>(command, ct);
    }
}
