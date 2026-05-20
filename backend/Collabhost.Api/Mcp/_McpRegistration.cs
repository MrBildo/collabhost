using Collabhost.Api.Platform;

using ModelContextProtocol.Protocol;

namespace Collabhost.Api.Mcp;

public static class McpRegistration
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddMcp()
        {
            // Per-call MCP authentication state. Both scoped. McpHeaderFallback caches the
            // X-User-Key header (if present) captured at session setup; McpRequestAuthenticator
            // runs at the top of each tool body. The header fallback exists strictly for
            // v1.0.x backward compatibility (Card #332); new clients supply authKey as a
            // per-call tool argument.
            services.AddScoped<McpHeaderFallback>();
            services.AddScoped<McpRequestAuthenticator>();

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
