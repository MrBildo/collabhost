using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class EnvironmentVariableMapping : IEntityTypeConfiguration<EnvironmentVariable>
{
    public void Configure(EntityTypeBuilder<EnvironmentVariable> builder)
    {
        builder.ToTable("EnvironmentVariable");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.AppId).IsRequired();

        // Env var names like PATH, NODE_ENV — 200 covers any realistic name
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();

        // Env var values can be long (PATH, connection strings) — 2000 provides generous headroom
        builder.Property(e => e.Value).HasMaxLength(2000).IsRequired();

        builder.HasIndex(e => new { e.AppId, e.Name }).IsUnique();

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");
    }
}
