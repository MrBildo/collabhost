using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public abstract class LookupEntityMapping<T> : IEntityTypeConfiguration<T> where T : LookupEntity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // Machine-readable code (e.g. "NpmPackage", "OnCrash") — 100 is generous for an enum-like name
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        // Human-readable label shown in UI (e.g. "NPM Package") — 200 matches convention for display names
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();

        // Optional explanatory text for the lookup value
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Ordinal);
        builder.Property(e => e.IsActive);

        // Shadow audit properties — never on the entity, managed by AuditInterceptor
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
