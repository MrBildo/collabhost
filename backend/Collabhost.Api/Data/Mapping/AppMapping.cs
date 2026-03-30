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

        // Slug used in domain routing (e.g. myapp.collab.internal) — 50 per spec
        builder.Property(e => e.Name)
            .HasConversion(v => v.Value, s => AppSlugValue.Create(s))
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();

        // Human-readable label shown in dashboard — 100 per spec
        builder.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(e => e.AppTypeId).IsRequired();

        // File system paths — 500 covers typical Windows MAX_PATH (260) with headroom
        builder.Property(e => e.InstallDirectory).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Port);
        builder.Property(e => e.IsStopped).HasDefaultValue(false);
        builder.Property(e => e.RegisteredAt);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
