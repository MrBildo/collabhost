using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Data;

public class CollabhostDbContext(DbContextOptions<CollabhostDbContext> options) : DbContext(options)
{
    public DbSet<App> Apps => Set<App>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollabhostDbContext).Assembly);
    }
}

public record AppLookup(Guid Id, string ExternalId, string DisplayName, Guid AppTypeId, string? UpdateCommand);

public static class CollabhostDbContextExtensions
{
    extension(CollabhostDbContext db)
    {
        public async Task<AppLookup?> FindAppByExternalIdAsync
        (
            string externalId,
            CancellationToken ct = default
        )
        {
            return await db.Database
                .SqlQuery<AppLookup>(
                    $"""
                    SELECT
                        A.[Id]
                        ,A.[ExternalId]
                        ,A.[DisplayName]
                        ,A.[AppTypeId]
                        ,A.[UpdateCommand]
                    FROM
                        [App] A
                    WHERE
                        A.[ExternalId] = {externalId}
                    """)
                .SingleOrDefaultAsync(ct);
        }
    }
}
