using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Lookups;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Collabhost.Api.Data.Mapping;

public class ProcessStateMapping : LookupEntityMapping<ProcessState>
{
    public override void Configure(EntityTypeBuilder<ProcessState> builder)
    {
        base.Configure(builder);

        builder.ToTable("ProcessState");

        builder.HasData
        (
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Stopped,
                Name = StringCatalog.ProcessStates.Stopped,
                DisplayName = "Stopped",
                Ordinal = 0,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Starting,
                Name = StringCatalog.ProcessStates.Starting,
                DisplayName = "Starting",
                Ordinal = 1,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Running,
                Name = StringCatalog.ProcessStates.Running,
                DisplayName = "Running",
                Ordinal = 2,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Stopping,
                Name = StringCatalog.ProcessStates.Stopping,
                DisplayName = "Stopping",
                Ordinal = 3,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Crashed,
                Name = StringCatalog.ProcessStates.Crashed,
                DisplayName = "Crashed",
                Ordinal = 4,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Restarting,
                Name = StringCatalog.ProcessStates.Restarting,
                DisplayName = "Restarting",
                Ordinal = 5,
                IsActive = true
            }
        );
    }
}
