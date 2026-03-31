using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.AppTypes;

public static class Get
{
    public record Response
    (
        string Id,
        string Name,
        string DisplayName,
        string? Description,
        bool IsBuiltIn,
        Dictionary<string, AppTypeCapabilityResponse> Capabilities
    );

    public static async Task<Results<Ok<Response>, NotFound>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAppTypeCommand(externalId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public record GetAppTypeCommand(string ExternalId) : ICommand<Get.Response>;

public sealed class GetAppTypeCommandHandler(CollabhostDbContext db)
    : ICommandHandler<GetAppTypeCommand, Get.Response>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<Get.Response>> HandleAsync
    (
        GetAppTypeCommand command,
        CancellationToken ct = default
    )
    {
        var appType = await _db.Set<AppType>()
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.ExternalId == command.ExternalId, ct);

        if (appType is null)
        {
            return CommandResult<Get.Response>.Fail("NOT_FOUND", "App type not found.");
        }

        var capabilityRows = await _db.Database
            .SqlQuery<AppTypeCapabilityRow>(
                $"""
                SELECT
                    C.[Slug] AS [CapabilitySlug]
                    ,C.[DisplayName] AS [CapabilityDisplayName]
                    ,C.[Category] AS [CapabilityCategory]
                    ,ATC.[Configuration]
                FROM
                    [AppTypeCapability] ATC
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                WHERE
                    ATC.[AppTypeId] = {appType.Id}
                ORDER BY
                    C.[Category], C.[Slug]
                """)
            .ToListAsync(ct);

        var capabilities = AppTypeCapabilityMapper.BuildCapabilityDictionary(capabilityRows);

        var response = new Get.Response
        (
            appType.ExternalId,
            appType.Name,
            appType.DisplayName,
            appType.Description,
            appType.IsBuiltIn,
            capabilities
        );

        return CommandResult<Get.Response>.Success(response);
    }
}
