using System.Reflection;

using Collabhost.Api.Common;

namespace Microsoft.Extensions.DependencyInjection;

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
