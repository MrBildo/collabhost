using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Data;

public class AppTypeConfiguration : IEntityTypeConfiguration<AppType>
{
    public void Configure(EntityTypeBuilder<AppType> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(t => t.Slug)
            .HasMaxLength(100);

        builder.HasIndex(t => t.Slug)
            .IsUnique();

        builder.Property(t => t.DisplayName)
            .HasMaxLength(200);

        builder.Property(t => t.Description)
            .HasMaxLength(500);

        builder.HasMany(t => t.Bindings)
            .WithOne()
            .HasForeignKey(b => b.AppTypeId);
    }
}
