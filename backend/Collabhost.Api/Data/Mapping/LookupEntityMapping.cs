using Collabhost.Api.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public abstract class LookupEntityMapping<T> : IEntityTypeConfiguration<T> where T : LookupEntity
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Ordinal);
        builder.Property(e => e.IsActive);

        // Shadow audit properties — never on the entity, managed by AuditInterceptor
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
