namespace Collabhost.Api.Data;

public record AppLookup(Guid Id, string ExternalId, string DisplayName, Guid AppTypeId);

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
                    FROM
                        [App] A
                    WHERE
                        A.[ExternalId] = {externalId}
                    """)
                .SingleOrDefaultAsync(ct);
        }
    }
}
