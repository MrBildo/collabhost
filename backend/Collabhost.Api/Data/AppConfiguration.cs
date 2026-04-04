using System.Globalization;

using Collabhost.Api.Registry;

namespace Collabhost.Api.Data;

public class AppConfiguration : IEntityTypeConfiguration<App>
{
    public void Configure(EntityTypeBuilder<App> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(a => a.Slug)
            .HasMaxLength(100);

        builder.HasIndex(a => a.Slug)
            .IsUnique();

        builder.Property(a => a.DisplayName)
            .HasMaxLength(200);

        builder.Property(a => a.AppTypeId)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.HasOne(a => a.AppType)
            .WithMany()
            .HasForeignKey(a => a.AppTypeId);
    }
}
