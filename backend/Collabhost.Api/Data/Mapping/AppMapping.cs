using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class AppMapping : IEntityTypeConfiguration<App>
{
    public void Configure(EntityTypeBuilder<App> builder)
    {
        builder.ToTable("Apps");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ExternalId).HasMaxLength(26).IsRequired();
        builder.HasIndex(e => e.ExternalId).IsUnique();

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();

        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.AppTypeId).IsRequired();
        builder.Property(e => e.InstallDirectory).HasMaxLength(500).IsRequired();
        builder.Property(e => e.CommandLine).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Arguments).HasMaxLength(1000);
        builder.Property(e => e.WorkingDirectory).HasMaxLength(500);
        builder.Property(e => e.RestartPolicyId).IsRequired();
        builder.Property(e => e.Port);
        builder.Property(e => e.HealthEndpoint).HasMaxLength(200);
        builder.Property(e => e.UpdateCommand).HasMaxLength(1000);
        builder.Property(e => e.AutoStart);
        builder.Property(e => e.RegisteredAt);

        // Child entity collection — EF Core auto-detects _environmentVariables backing field
        builder.HasMany(e => e.EnvironmentVariables)
            .WithOne()
            .HasForeignKey(e => e.AppId)
            .OnDelete(DeleteBehavior.Cascade);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
