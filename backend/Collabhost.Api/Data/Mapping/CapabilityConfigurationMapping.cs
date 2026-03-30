using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class CapabilityConfigurationMapping : IEntityTypeConfiguration<CapabilityConfiguration>
{
    public void Configure(EntityTypeBuilder<CapabilityConfiguration> builder)
    {
        builder.ToTable("CapabilityConfiguration");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AppId).IsRequired();
        builder.Property(e => e.AppTypeCapabilityId).IsRequired();

        // JSON configuration — unbounded text to allow complex overrides
        builder.Property(e => e.Configuration).IsRequired();

        builder.HasIndex(e => new { e.AppId, e.AppTypeCapabilityId }).IsUnique();

        builder.HasOne<App>()
            .WithMany()
            .HasForeignKey(e => e.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AppTypeCapability>()
            .WithMany()
            .HasForeignKey(e => e.AppTypeCapabilityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
