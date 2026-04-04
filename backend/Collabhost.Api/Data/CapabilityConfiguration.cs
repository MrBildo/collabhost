using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;

namespace Collabhost.Api.Data;

public class CapabilityBindingConfiguration : IEntityTypeConfiguration<CapabilityBinding>
{
    public void Configure(EntityTypeBuilder<CapabilityBinding> builder)
    {
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(b => b.AppTypeId)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(b => b.CapabilitySlug)
            .HasMaxLength(50);

        builder.HasIndex(b => new { b.AppTypeId, b.CapabilitySlug })
            .IsUnique();
    }
}

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
