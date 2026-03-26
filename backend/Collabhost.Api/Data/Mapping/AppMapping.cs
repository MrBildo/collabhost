using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Values;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class AppMapping : IEntityTypeConfiguration<App>
{
    public void Configure(EntityTypeBuilder<App> builder)
    {
        builder.ToTable("App");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // ULID string representation is always 26 characters
        builder.Property(e => e.ExternalId).HasMaxLength(26).IsRequired();
        builder.HasIndex(e => e.ExternalId).IsUnique();

        // Slug used in domain routing (e.g. myapp.collab.internal) — 100 is generous for a hostname-safe slug
        builder.Property(e => e.Name)
            .HasConversion(v => v.Value, s => AppSlugValue.Create(s))
            .HasMaxLength(100)
            .IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();

        // Human-readable label shown in dashboard
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.AppTypeId).IsRequired();

        // File system paths — 500 covers typical Windows MAX_PATH (260) with headroom
        builder.Property(e => e.InstallDirectory).HasMaxLength(500).IsRequired();
        builder.Property(e => e.CommandLine).HasMaxLength(500).IsRequired();

        // Arguments can be longer than a single path (multiple flags, quoted strings)
        builder.Property(e => e.Arguments).HasMaxLength(1000);

        // Working directory is a file system path, same rationale as InstallDirectory
        builder.Property(e => e.WorkingDirectory).HasMaxLength(500);
        builder.Property(e => e.RestartPolicyId).IsRequired();
        builder.Property(e => e.Port);

        // Relative URL path like /health or /api/status — 200 is ample
        builder.Property(e => e.HealthEndpoint).HasMaxLength(200);

        // Shell command to run updates — can include args, so 1000 like Arguments
        builder.Property(e => e.UpdateCommand).HasMaxLength(1000);
        builder.Property(e => e.AutoStart);
        builder.Property(e => e.RegisteredAt);

        // Child entity collection — EF Core auto-detects _environmentVariables backing field
        builder.HasMany(e => e.EnvironmentVariables)
            .WithOne()
            .HasForeignKey(e => e.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
