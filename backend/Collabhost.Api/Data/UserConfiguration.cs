using System.Globalization;

using Collabhost.Api.Authorization;

namespace Collabhost.Api.Data;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(u => u.Name)
            .HasMaxLength(200);

        builder.Property(u => u.AuthKey)
            .HasMaxLength(26);

        builder.HasIndex(u => u.AuthKey)
            .IsUnique();
    }
}
