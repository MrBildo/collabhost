using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class DiscoveryStrategyMapping : LookupEntityMapping<DiscoveryStrategy>
{
    public override void Configure(EntityTypeBuilder<DiscoveryStrategy> builder)
    {
        base.Configure(builder);

        builder.ToTable("DiscoveryStrategy");

        builder.HasData
        (
            new DiscoveryStrategy
            {
                Id = IdentifierCatalog.DiscoveryStrategies.DotNetRuntimeConfig,
                Name = StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig,
                DisplayName = ".NET Runtime Config",
                Ordinal = 0,
                IsActive = true
            },
            new DiscoveryStrategy
            {
                Id = IdentifierCatalog.DiscoveryStrategies.PackageJson,
                Name = StringCatalog.DiscoveryStrategies.PackageJson,
                DisplayName = "package.json",
                Ordinal = 1,
                IsActive = true
            },
            new DiscoveryStrategy
            {
                Id = IdentifierCatalog.DiscoveryStrategies.Manual,
                Name = StringCatalog.DiscoveryStrategies.Manual,
                DisplayName = "Manual",
                Ordinal = 2,
                IsActive = true
            }
        );
    }
}
