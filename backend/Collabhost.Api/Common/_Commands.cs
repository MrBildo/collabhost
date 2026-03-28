using System.Reflection;

using Collabhost.Api.Common;

namespace Collabhost.Api.Common
{
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

    public sealed class CommandDispatcher(IServiceProvider serviceProvider)
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        public async Task<CommandResult<TResult>> DispatchAsync<TCommand, TResult>(TCommand command, CancellationToken ct = default)
            where TCommand : ICommand<TResult>
        {
            var handler = _serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
            return await handler.HandleAsync(command, ct);
        }

        public async Task<CommandResult<TResult>> DispatchAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        {
            var commandType = command.GetType();
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, typeof(TResult));
            var handler = _serviceProvider.GetRequiredService(handlerType);
            var method = handlerType.GetMethod("HandleAsync")!;
            return await (Task<CommandResult<TResult>>)method.Invoke(handler, [command, ct])!;
        }

        public async Task<CommandResult<Empty>> DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default)
            where TCommand : ICommand<Empty> => await DispatchAsync<TCommand, Empty>(command, ct);
    }
}

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CommandServiceExtensions
    {
        extension(IServiceCollection services)
        {
            public IServiceCollection AddCommandDispatcher()
            {
                services.AddScoped<CommandDispatcher>();

                var handlerTypes = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .SelectMany(
                        t => t.GetInterfaces()
                            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                            .Select(i => new { Interface = i, Implementation = t })
                    );

                foreach (var handler in handlerTypes)
                {
                    services.AddScoped(handler.Interface, handler.Implementation);
                }

                return services;
            }
        }
    }
}
