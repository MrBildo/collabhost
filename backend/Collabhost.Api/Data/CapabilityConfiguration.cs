using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Data;

public class CapabilityOverrideConfiguration : IEntityTypeConfiguration<CapabilityOverride>
{
    public void Configure(EntityTypeBuilder<CapabilityOverride> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(o => o.AppId)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(o => o.CapabilitySlug)
            .HasMaxLength(50);

        builder.HasIndex(o => new { o.AppId, o.CapabilitySlug })
            .IsUnique();

        builder.HasOne<App>()
            .WithMany()
            .HasForeignKey(o => o.AppId);
    }
}
