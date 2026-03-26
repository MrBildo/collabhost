using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class RestartPolicyMapping : LookupEntityMapping<RestartPolicy>
{
    public override void Configure(EntityTypeBuilder<RestartPolicy> builder)
    {
        base.Configure(builder);

        builder.ToTable("RestartPolicies");

        builder.HasData
        (
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.Never,
                Name = "Never",
                DisplayName = StringCatalog.RestartPolicies.Never,
                Ordinal = 0,
                IsActive = true
            },
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.OnCrash,
                Name = "OnCrash",
                DisplayName = StringCatalog.RestartPolicies.OnCrash,
                Ordinal = 1,
                IsActive = true
            },
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.Always,
                Name = "Always",
                DisplayName = StringCatalog.RestartPolicies.Always,
                Ordinal = 2,
                IsActive = true
            }
        );
    }
}
