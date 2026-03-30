using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

#pragma warning disable MA0051 // Long method justified — seed data for all app type capabilities
public class AppTypeCapabilityMapping : IEntityTypeConfiguration<AppTypeCapability>
{
    public void Configure(EntityTypeBuilder<AppTypeCapability> builder)
    {
        builder.ToTable("AppTypeCapability");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AppTypeId).IsRequired();
        builder.Property(e => e.CapabilityId).IsRequired();

        // JSON configuration — unbounded text to allow complex configs
        builder.Property(e => e.Configuration).IsRequired();

        builder.HasIndex(e => new { e.AppTypeId, e.CapabilityId }).IsUnique();

        builder.HasOne<AppType>()
            .WithMany()
            .HasForeignKey(e => e.AppTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Capability>()
            .WithMany()
            .HasForeignKey(e => e.CapabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");

        SeedData(builder);
    }

    private static void SeedData(EntityTypeBuilder<AppTypeCapability> builder)
    {
        var id = 1; // Counter for generating deterministic Guids

        // --- ASP.NET Core (DotNetApp) ---
        builder.HasData
        (
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"dotnet-runtimeconfig","gracefulShutdown":true,"shutdownTimeoutSeconds":30}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.PortInjection, """{"envVar":"ASPNETCORE_URLS","format":"http://localhost:{port}"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.HealthCheck, """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5,"retries":3}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.EnvironmentDefaults, """{"defaults":{"ASPNETCORE_ENVIRONMENT":"Production"}}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Restart, """{"policy":"always"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":true}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.AspNetRuntime, """{"targetFramework":"net10.0","runtimeVersion":"10.0.x","selfContained":false}""")
        );

        // --- Node.js App ---
        builder.HasData
        (
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"package-json","gracefulShutdown":true,"shutdownTimeoutSeconds":30}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.PortInjection, """{"envVar":"PORT","format":"{port}"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.HealthCheck, """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5,"retries":3}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Restart, """{"policy":"always"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":true}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.NodeRuntime, """{"nodeVersion":"22.x","packageManager":"npm"}""")
        );

        // --- Executable ---
        builder.HasData
        (
            CreateSeeded(id++, IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"manual","gracefulShutdown":false,"shutdownTimeoutSeconds":10}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.PortInjection, """{"envVar":"PORT","format":"{port}"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Restart, """{"policy":"onCrash"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":false}""")
        );

        // --- React App ---
        builder.HasData
        (
            CreateSeeded(id++, IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"fileServer","spaFallback":true}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.NodeRuntime, """{"nodeVersion":"22.x","packageManager":"npm","buildCommand":"npm run build"}"""),
            CreateSeeded(id++, IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.ReactRuntime, """{"version":"18.x","router":"react-router","bundler":"vite"}""")
        );

        // --- Static Site ---
        builder.HasData
        (
            CreateSeeded(id, IdentifierCatalog.AppTypes.StaticSite, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"fileServer","spaFallback":false}""")
        );
    }

    private static AppTypeCapability CreateSeeded
    (
        int seedIndex,
        Guid appTypeId,
        Guid capabilityId,
        string configuration
    )
    {
        // Generate deterministic Guids from seed index for repeatable migrations
        var hexIndex = seedIndex.ToString("X12", System.Globalization.CultureInfo.InvariantCulture);
        var deterministicId = new Guid($"d0d0d0d0-aeed-4000-a000-{hexIndex}");
        return AppTypeCapability.CreateSeeded(deterministicId, appTypeId, capabilityId, configuration);
    }
}
#pragma warning restore MA0051
