using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class RestartPolicyMapping : LookupEntityMapping<RestartPolicy>
{
    public override void Configure(EntityTypeBuilder<RestartPolicy> builder)
    {
        base.Configure(builder);

        builder.ToTable("RestartPolicy");

        builder.HasData
        (
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.Never,
                Name = StringCatalog.RestartPolicies.Never,
                DisplayName = "Never",
                Ordinal = 0,
                IsActive = true
            },
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.OnCrash,
                Name = StringCatalog.RestartPolicies.OnCrash,
                DisplayName = "On Crash",
                Ordinal = 1,
                IsActive = true
            },
            new RestartPolicy
            {
                Id = IdentifierCatalog.RestartPolicies.Always,
                Name = StringCatalog.RestartPolicies.Always,
                DisplayName = "Always",
                Ordinal = 2,
                IsActive = true
            }
        );
    }
}
