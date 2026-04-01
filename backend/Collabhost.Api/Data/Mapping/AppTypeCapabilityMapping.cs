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
        // --- ASP.NET Core (DotNetApp) ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("61e355eb-1998-41ad-bfdc-069b643173c1"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"dotnet-runtimeconfig","gracefulShutdown":true,"shutdownTimeoutSeconds":30}"""),
            AppTypeCapability.CreateSeeded(new Guid("6d0d10bb-0764-477d-a113-b3c1f380f598"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.PortInjection, """{"environmentVariableName":"ASPNETCORE_URLS","portFormat":"http://localhost:{port}"}"""),
            AppTypeCapability.CreateSeeded(new Guid("41b486b0-037f-44e8-9a96-028c490fa48c"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            AppTypeCapability.CreateSeeded(new Guid("f82da976-2c10-428c-a192-6ebee06107ab"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.HealthCheck, """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5,"retries":3}"""),
            AppTypeCapability.CreateSeeded(new Guid("7eb34403-7641-49aa-8a0c-7f30a39d2355"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.EnvironmentDefaults, """{"defaults":{"ASPNETCORE_ENVIRONMENT":"Production"}}"""),
            AppTypeCapability.CreateSeeded(new Guid("3cf3ba43-bb6a-4823-ac18-2747c57c802f"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.Restart, """{"policy":"always"}"""),
            AppTypeCapability.CreateSeeded(new Guid("fcd84bc2-a09c-4e1b-a1e8-2a660a0d3113"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":true}"""),
            AppTypeCapability.CreateSeeded(new Guid("097ade9a-ab92-4a0f-ac1e-51d58e1d37cc"), IdentifierCatalog.AppTypes.DotNetApp, IdentifierCatalog.Capabilities.AspNetRuntime, """{"targetFramework":"net10.0","runtimeVersion":"10.0.x","selfContained":false}""")
        );

        // --- Node.js App ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("f51fc05c-924b-4f6d-b0e0-8193b676a6f5"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"package-json","gracefulShutdown":true,"shutdownTimeoutSeconds":30}"""),
            AppTypeCapability.CreateSeeded(new Guid("74ff801f-58f8-4fc6-8faf-bdddecd4673e"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.PortInjection, """{"environmentVariableName":"PORT","portFormat":"{port}"}"""),
            AppTypeCapability.CreateSeeded(new Guid("74137008-620c-46cc-ba2c-e1e1896d25c1"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            AppTypeCapability.CreateSeeded(new Guid("a5792083-b9e7-4ef7-8f3f-b9835d908362"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.HealthCheck, """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5,"retries":3}"""),
            AppTypeCapability.CreateSeeded(new Guid("088e99f8-dd64-4d14-bed2-e0e2027ac1b4"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.Restart, """{"policy":"always"}"""),
            AppTypeCapability.CreateSeeded(new Guid("909f7e6d-451c-4bff-b8d5-bfb04f3b5116"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":true}"""),
            AppTypeCapability.CreateSeeded(new Guid("84810cdc-4299-42f1-903d-8fff82ed4e92"), IdentifierCatalog.AppTypes.NodeApp, IdentifierCatalog.Capabilities.NodeRuntime, """{"nodeVersion":"22.x","packageManager":"npm"}""")
        );

        // --- Executable ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("ecb72690-91c9-43ae-9039-92ada963271c"), IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"manual","gracefulShutdown":false,"shutdownTimeoutSeconds":10,"command":"echo","arguments":"no command configured"}"""),
            AppTypeCapability.CreateSeeded(new Guid("254d4414-246f-44e5-82d0-0075b3f994c0"), IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.PortInjection, """{"environmentVariableName":"PORT","portFormat":"{port}"}"""),
            AppTypeCapability.CreateSeeded(new Guid("fa660eb9-810a-47c1-8010-799481c4dca5"), IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"reverseProxy"}"""),
            AppTypeCapability.CreateSeeded(new Guid("14cb559d-7675-43bb-acdd-f1f15671c570"), IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.Restart, """{"policy":"onCrash"}"""),
            AppTypeCapability.CreateSeeded(new Guid("408ae72c-0771-4817-a5ed-950419ee5771"), IdentifierCatalog.AppTypes.Executable, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":false}""")
        );

        // --- React App ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("e708632b-d307-4045-9778-679d979b1578"), IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"fileServer","spaFallback":true}"""),
            AppTypeCapability.CreateSeeded(new Guid("d9c3ac3e-052c-4992-ac36-bd3079499663"), IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.NodeRuntime, """{"nodeVersion":"22.x","packageManager":"npm","buildCommand":"npm run build"}"""),
            AppTypeCapability.CreateSeeded(new Guid("b256a4b1-86fe-46db-ab50-0061e7854996"), IdentifierCatalog.AppTypes.ReactApp, IdentifierCatalog.Capabilities.ReactRuntime, """{"reactVersion":"18.x","router":"react-router","bundler":"vite"}""")
        );

        // --- Static Site ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("a4cbac96-a44d-4823-9924-e4a530ee96b2"), IdentifierCatalog.AppTypes.StaticSite, IdentifierCatalog.Capabilities.Routing, """{"domainPattern":"{slug}.collab.internal","serveMode":"fileServer","spaFallback":false}""")
        );

        // --- System Service ---
        builder.HasData
        (
            AppTypeCapability.CreateSeeded(new Guid("c1d2e3f4-a5b6-4c7d-8e9f-0a1b2c3d4e5f"), IdentifierCatalog.AppTypes.SystemService, IdentifierCatalog.Capabilities.Process, """{"discoveryStrategy":"manual","gracefulShutdown":true,"shutdownTimeoutSeconds":10,"command":"echo","arguments":"no command configured"}"""),
            AppTypeCapability.CreateSeeded(new Guid("d2e3f4a5-b6c7-4d8e-9f0a-1b2c3d4e5f6a"), IdentifierCatalog.AppTypes.SystemService, IdentifierCatalog.Capabilities.Restart, """{"policy":"onCrash"}"""),
            AppTypeCapability.CreateSeeded(new Guid("e3f4a5b6-c7d8-4e9f-0a1b-2c3d4e5f6a7b"), IdentifierCatalog.AppTypes.SystemService, IdentifierCatalog.Capabilities.AutoStart, """{"enabled":true}""")
        );
    }
}
#pragma warning restore MA0051
