using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class AppTypeMapping : LookupEntityMapping<AppType>
{
    public override void Configure(EntityTypeBuilder<AppType> builder)
    {
        base.Configure(builder);

        builder.ToTable("AppTypes");

        builder.HasData
        (
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.Executable,
                Name = "Executable",
                DisplayName = StringCatalog.AppTypes.Executable,
                Ordinal = 0,
                IsActive = true
            },
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.NpmPackage,
                Name = "NpmPackage",
                DisplayName = StringCatalog.AppTypes.NpmPackage,
                Ordinal = 1,
                IsActive = true
            },
            new AppType
            {
                Id = IdentifierCatalog.AppTypes.StaticSite,
                Name = "StaticSite",
                DisplayName = StringCatalog.AppTypes.StaticSite,
                Ordinal = 2,
                IsActive = true
            }
        );
    }
}
