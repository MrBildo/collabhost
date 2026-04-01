using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class ServeModeMapping : LookupEntityMapping<ServeMode>
{
    public override void Configure(EntityTypeBuilder<ServeMode> builder)
    {
        base.Configure(builder);

        builder.ToTable("ServeMode");

        builder.HasData
        (
            new ServeMode
            {
                Id = IdentifierCatalog.ServeModes.ReverseProxy,
                Name = StringCatalog.ServeModes.ReverseProxy,
                DisplayName = "Reverse Proxy",
                Ordinal = 0,
                IsActive = true
            },
            new ServeMode
            {
                Id = IdentifierCatalog.ServeModes.FileServer,
                Name = StringCatalog.ServeModes.FileServer,
                DisplayName = "File Server",
                Ordinal = 1,
                IsActive = true
            }
        );
    }
}
