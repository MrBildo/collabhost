using Collabhost.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Collabhost.Api.Data;

public class CollabhostDbContext(DbContextOptions<CollabhostDbContext> options) : DbContext(options)
{
    public DbSet<App> Apps => Set<App>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollabhostDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) => configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>();
}

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    convertToProviderExpression: v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    convertFromProviderExpression: v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
);

public record AppLookup(Guid Id, string ExternalId, string DisplayName, Guid AppTypeId, string? UpdateCommand);

public static class CollabhostDbContextExtensions
{
    extension(CollabhostDbContext db)
    {
        public async Task<AppLookup?> FindAppByExternalIdAsync
        (
            string externalId,
            CancellationToken ct = default
        ) => await db.Database
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
