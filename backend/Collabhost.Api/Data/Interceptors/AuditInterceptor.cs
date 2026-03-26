using Collabhost.Api.Domain;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Collabhost.Api.Data.Interceptors;

public class AuditInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync
    (
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var now = DateTime.UtcNow;

        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.Entity is not Entity and not LookupEntity)
            {
                continue;
            }

            if (entry.State is EntityState.Added)
            {
                entry.Property("CreatedAt").CurrentValue = now;
                entry.Property("UpdatedAt").CurrentValue = now;
            }
            else if (entry.State is EntityState.Modified)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
