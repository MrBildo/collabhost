using System.Reflection;

using ModelContextProtocol.Protocol;

namespace Collabhost.Api.Mcp;

public static class McpRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMcp()
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0";

            services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "collabhost",
                        Version = version
                    };

                    options.ServerInstructions = McpServerInstructions.Content;
                })
                .WithHttpTransport(options =>
                {
                    options.Stateless = true;
                    options.ConfigureSessionOptions = McpAuthentication.ConfigureSessionAsync;
                })
                .WithTools<DiscoveryTools>()
                .WithTools<LifecycleTools>()
                .WithTools<ConfigurationTools>()
                .WithTools<RegistrationTools>();

            return services;
        }
    }

    extension(WebApplication app)
    {
        public WebApplication MapMcpEndpoints()
        {
            app.MapMcp("/mcp");
            return app;
        }
    }
}
