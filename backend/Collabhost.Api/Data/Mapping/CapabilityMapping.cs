using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class CapabilityMapping : IEntityTypeConfiguration<Capability>
{
    public void Configure(EntityTypeBuilder<Capability> builder)
    {
        builder.ToTable("Capability");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // Kebab-case identifier — 50 per spec
        builder.Property(e => e.Slug).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.Slug).IsUnique();

        // Human-readable — 100 per spec
        builder.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();

        // Optional description — 500 per spec
        builder.Property(e => e.Description).HasMaxLength(500);

        // "behavioral" or "informational" — 20 per spec
        builder.Property(e => e.Category).HasMaxLength(20).IsRequired();

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");

        builder.HasData
        (
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.Process, StringCatalog.Capabilities.Process, "Process Management", "How the app's process is discovered, started, and stopped", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.PortInjection, StringCatalog.Capabilities.PortInjection, "Port Injection", "How the platform communicates the assigned port to the app process", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.Routing, StringCatalog.Capabilities.Routing, "Routing", "How traffic reaches the app through the reverse proxy", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.HealthCheck, StringCatalog.Capabilities.HealthCheck, "Health Check", "HTTP endpoint polled to determine app health", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.EnvironmentDefaults, StringCatalog.Capabilities.EnvironmentDefaults, "Environment Variables", "Environment variables injected when the app process starts", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.Restart, StringCatalog.Capabilities.Restart, "Restart Policy", "How the platform responds when the app process exits unexpectedly", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.AutoStart, StringCatalog.Capabilities.AutoStart, "Auto Start", "Whether the app starts automatically when Collabhost starts", "behavioral"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.AspNetRuntime, StringCatalog.Capabilities.AspNetRuntime, "ASP.NET Runtime", ".NET runtime and framework version information", "informational"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.NodeRuntime, StringCatalog.Capabilities.NodeRuntime, "Node.js Runtime", "Node.js version and package manager information", "informational"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.ReactRuntime, StringCatalog.Capabilities.ReactRuntime, "React", "React framework and tooling information", "informational"),
            Capability.CreateSeeded(IdentifierCatalog.Capabilities.Artifact, StringCatalog.Capabilities.Artifact, "Artifact", "Where the app's files are located on the host filesystem", "behavioral")
        );
    }
}
