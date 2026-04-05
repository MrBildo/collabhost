using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Data;

#pragma warning disable CA1305, MA0011, MA0076 // Ulid.Parse/ToString are not locale-sensitive
public static class SeedData
{
    // Fixed timestamp for seed data — avoids EF PendingModelChangesWarning from DateTime.UtcNow defaults
    private static readonly DateTime _seedTimestamp = new(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder modelBuilder)
    {
        var appTypes = SeedAppTypes(modelBuilder);

        SeedDotNetBindings(modelBuilder, appTypes.DotNet);
        SeedNodeJsBindings(modelBuilder, appTypes.NodeJs);
        SeedStaticSiteBindings(modelBuilder, appTypes.StaticSite);
        SeedSystemServiceBindings(modelBuilder, appTypes.SystemService);
        SeedExecutableBindings(modelBuilder, appTypes.Executable);
    }

    private static
    (
        AppType DotNet,
        AppType NodeJs,
        AppType StaticSite,
        AppType SystemService,
        AppType Executable
    )
    SeedAppTypes(ModelBuilder modelBuilder)
    {
        var dotnetApp = new AppType
        {
            Id = Ulid.Parse("01KN8K1MRQ0K06ADYJJ8VAXG5Y"),
            Slug = "dotnet-app",
            DisplayName = ".NET Application",
            Description = "ASP.NET Core or .NET console application",
            IsBuiltIn = true,
            CreatedAt = _seedTimestamp,
            MetadataJson = """{"runtime":{"name":".NET","version":"10","targetFramework":"net10.0"}}"""
        };

        var nodejsApp = new AppType
        {
            Id = Ulid.Parse("01KN8K1MRT4XGXXW5BBQ8YZNN2"),
            Slug = "nodejs-app",
            DisplayName = "Node.js Application",
            Description = "Server-side JavaScript with npm",
            IsBuiltIn = true,
            CreatedAt = _seedTimestamp,
            MetadataJson = """{"runtime":{"name":"Node.js","version":"22","packageManager":"npm"}}"""
        };

        var staticSite = new AppType
        {
            Id = Ulid.Parse("01KN8K1MRT26VCX65J1ZSVWESB"),
            Slug = "static-site",
            DisplayName = "Static Site",
            Description = "Static files served by Caddy",
            IsBuiltIn = true,
            CreatedAt = _seedTimestamp,
            MetadataJson = null
        };

        var systemService = new AppType
        {
            Id = Ulid.Parse("01KNA0A0ZN42VV6T9GTEPS17CD"),
            Slug = "system-service",
            DisplayName = "System Service",
            Description = "Infrastructure process with no routing or port injection",
            IsBuiltIn = true,
            CreatedAt = _seedTimestamp,
            MetadataJson = null
        };

        var executable = new AppType
        {
            Id = Ulid.Parse("01KNA0A0ZRZE6W7RPX9BRREKNQ"),
            Slug = "executable",
            DisplayName = "Executable",
            Description = "Generic binary with port injection and reverse proxy routing",
            IsBuiltIn = true,
            CreatedAt = _seedTimestamp,
            MetadataJson = null
        };

        modelBuilder.Entity<AppType>().HasData
        (
            dotnetApp,
            nodejsApp,
            staticSite,
            systemService,
            executable
        );

        return (dotnetApp, nodejsApp, staticSite, systemService, executable);
    }

    private static void SeedDotNetBindings(ModelBuilder modelBuilder, AppType dotnetApp) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTP60DVWP6ERZ8R4F9"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTJD1NCG0J9R4364MJ"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"DotNetRuntimeConfiguration","shutdownTimeoutSeconds":30}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTCFS85XS4TRW6EGSR"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "port-injection",
                DefaultConfigurationJson = """{"environmentVariableName":"ASPNETCORE_URLS","portFormat":"http://localhost:{port}"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRT34CN63B8QZ96N3Q7"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"ReverseProxy","spaFallback":false}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTG49PHRKY1N3DMFKN"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "health-check",
                DefaultConfigurationJson = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTGF8GQ3X2CFS2JCQS"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRT1SW33ZS6DK4TTGKB"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":true}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRT0B5TF2TTXV68DAFJ"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "environment-defaults",
                DefaultConfigurationJson = """{"variables":{"ASPNETCORE_ENVIRONMENT":"Production","DOTNET_ENVIRONMENT":"Production","DOTNET_NOLOGO":"1","DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION":"true"}}"""
            }
        );

    private static void SeedNodeJsBindings(ModelBuilder modelBuilder, AppType nodejsApp) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTHFBR5P75WEE5K3NT"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRT9RES6FCWSNFYNXGK"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"PackageJson","shutdownTimeoutSeconds":15}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTD4TJKKDPGHG36Z4K"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "port-injection",
                DefaultConfigurationJson = """{"environmentVariableName":"PORT","portFormat":"{port}"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTGPRVSG3F6EJBB8CM"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"ReverseProxy","spaFallback":false}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTEFCM6C0ZXM6GFM68"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "health-check",
                DefaultConfigurationJson = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRT9D970Y0XZR74W1Z1"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTT086R433KGMBT21A"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":true}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTZZ9PJ1QMZSG5QHRE"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "environment-defaults",
                DefaultConfigurationJson = """{"variables":{"NODE_ENV":"production"}}"""
            }
        );

    private static void SeedStaticSiteBindings(ModelBuilder modelBuilder, AppType staticSite) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTE14RGEAS4VDD44P3"),
                AppTypeId = staticSite.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KN8K1MRTETFY88Z8FTJGCBB5"),
                AppTypeId = staticSite.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"FileServer","spaFallback":false}"""
            }
        );

    private static void SeedSystemServiceBindings(ModelBuilder modelBuilder, AppType systemService) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRKQQ4TJK7E2Z3RX4J"),
                AppTypeId = systemService.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRDCSEHPT4E069WFQS"),
                AppTypeId = systemService.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"Manual","shutdownTimeoutSeconds":10}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRFWXKV2HS4TS5ACA7"),
                AppTypeId = systemService.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRYZN3ZPXS6ENHHHH5"),
                AppTypeId = systemService.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":true}"""
            }
        );

    private static void SeedExecutableBindings(ModelBuilder modelBuilder, AppType executable) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRD5350M0C0Y8AZ62V"),
                AppTypeId = executable.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZR0AAGHBHWGZND1C0J"),
                AppTypeId = executable.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"Manual","shutdownTimeoutSeconds":10}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRVMFJBHFQ623KG0SM"),
                AppTypeId = executable.Id,
                CapabilitySlug = "port-injection",
                DefaultConfigurationJson = """{"environmentVariableName":"PORT","portFormat":"{port}"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZR1VEJ6DCGFGS4M97Q"),
                AppTypeId = executable.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"ReverseProxy","spaFallback":false}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZRY7GQTTCX8HY6S97W"),
                AppTypeId = executable.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01KNA0A0ZR2V1V5VMMR6TR8BPC"),
                AppTypeId = executable.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":false}"""
            }
        );
}
#pragma warning restore CA1305, MA0011, MA0076
