using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class AppTypeMapping : LookupEntityMapping<AppType>
{
    public override void Configure(EntityTypeBuilder<AppType> builder)
    {
        base.Configure(builder);

        builder.ToTable("AppType");

        builder.HasData
        (
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.Executable,
                Name = StringCatalog.AppTypes.Executable,
                DisplayName = "Executable",
                Ordinal = 0,
                IsActive = true
            },
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.NpmPackage,
                Name = StringCatalog.AppTypes.NpmPackage,
                DisplayName = "NPM Package",
                Ordinal = 1,
                IsActive = true
            },
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.StaticSite,
                Name = StringCatalog.AppTypes.StaticSite,
                DisplayName = "Static Site",
                Ordinal = 2,
                IsActive = true
            }
        );
    }
}
