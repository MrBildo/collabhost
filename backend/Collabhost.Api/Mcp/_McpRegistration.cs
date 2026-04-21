using Collabhost.Api.Platform;

using ModelContextProtocol.Protocol;

namespace Collabhost.Api.Mcp;

public static class McpRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMcp()
        {
            services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "collabhost",
                        Version = VersionInfo.Current
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
                .WithTools<RegistrationTools>()
                .WithTools<ActivityLogTools>();

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
