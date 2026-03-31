using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class AppTypeMapping : IEntityTypeConfiguration<AppType>
{
    public void Configure(EntityTypeBuilder<AppType> builder)
    {
        builder.ToTable("AppType");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // ULID string representation is always 26 characters
        builder.Property(e => e.ExternalId).HasMaxLength(26).IsRequired();
        builder.HasIndex(e => e.ExternalId).IsUnique();

        // Slug format — 50 per spec
        builder.Property(e => e.Name).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.Name).IsUnique();

        // Human-readable — 100 per spec
        builder.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();

        // Optional description — 500 per spec
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.Property(e => e.IsBuiltIn).HasDefaultValue(false);

        // Shadow audit properties
        builder.Property<DateTime>("CreatedAt").HasDefaultValueSql("datetime('now')");
        builder.Property<DateTime>("UpdatedAt").HasDefaultValueSql("datetime('now')");

        builder.HasData
        (
            AppType.CreateSeeded(IdentifierCatalog.AppTypes.DotNetApp, "01KN0P7JYNYACWC35R77C1KTV2", StringCatalog.AppTypes.DotNetApp, "ASP.NET Core", ".NET web application hosted via Kestrel", true),
            AppType.CreateSeeded(IdentifierCatalog.AppTypes.NodeApp, "01KN0P7JYNRBD8DC9DMKEDJX2M", StringCatalog.AppTypes.NodeApp, "Node.js", "Node.js application", true),
            AppType.CreateSeeded(IdentifierCatalog.AppTypes.Executable, "01KN0P7JYNJRAHGC01N17NFTWW", StringCatalog.AppTypes.Executable, "Executable", "Generic executable process", true),
            AppType.CreateSeeded(IdentifierCatalog.AppTypes.ReactApp, "01KN0P7JYNM6PJP07XTAXK77GR", StringCatalog.AppTypes.ReactApp, "React App", "React single-page application served as static files", true),
            AppType.CreateSeeded(IdentifierCatalog.AppTypes.StaticSite, "01KN0P7JYN9TDB3SPPS25Z493F", StringCatalog.AppTypes.StaticSite, "Static Site", "Static files served directly by the reverse proxy", true)
        );
    }
}
