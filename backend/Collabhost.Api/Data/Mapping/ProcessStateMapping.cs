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
                Name = "Stopped",
                DisplayName = StringCatalog.ProcessStates.Stopped,
                Ordinal = 0,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Starting,
                Name = "Starting",
                DisplayName = StringCatalog.ProcessStates.Starting,
                Ordinal = 1,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Running,
                Name = "Running",
                DisplayName = StringCatalog.ProcessStates.Running,
                Ordinal = 2,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Stopping,
                Name = "Stopping",
                DisplayName = StringCatalog.ProcessStates.Stopping,
                Ordinal = 3,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Crashed,
                Name = "Crashed",
                DisplayName = StringCatalog.ProcessStates.Crashed,
                Ordinal = 4,
                IsActive = true
            },
            new ProcessState
            {
                Id = IdentifierCatalog.ProcessStates.Restarting,
                Name = "Restarting",
                DisplayName = StringCatalog.ProcessStates.Restarting,
                Ordinal = 5,
                IsActive = true
            }
        );
    }
}
