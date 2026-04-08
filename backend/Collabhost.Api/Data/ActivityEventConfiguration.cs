using System.Globalization;

using Collabhost.Api.ActivityLog;

namespace Collabhost.Api.Data;

public class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasConversion
            (
                v => v.ToString(null, CultureInfo.InvariantCulture),
                v => Ulid.Parse(v, CultureInfo.InvariantCulture)
            )
            .HasMaxLength(26);

        builder.Property(e => e.EventType).HasMaxLength(50);

        builder.Property(e => e.ActorId).HasMaxLength(26);

        builder.Property(e => e.ActorName).HasMaxLength(200);

        builder.Property(e => e.AppId).HasMaxLength(26);

        builder.Property(e => e.AppSlug).HasMaxLength(100);

        builder.HasIndex(e => e.Timestamp);

        builder.HasIndex(e => e.AppSlug);

        builder.HasIndex(e => e.EventType);

        builder.HasIndex(e => e.ActorId);
    }
}
