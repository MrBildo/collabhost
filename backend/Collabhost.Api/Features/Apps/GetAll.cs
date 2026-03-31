using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.Apps;

public static class GetAll
{
    public static async Task<Ok<List<AppDetailResponse>>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAllAppsCommand(), ct);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetAllAppsCommand : ICommand<List<AppDetailResponse>>;

#pragma warning disable MA0051 // Long method justified — bridge aggregation across DB and core systems
public sealed class GetAllAppsCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    ProxyConfigManager proxyConfigManager,
    ICapabilityBridge capabilityBridge
) : ICommandHandler<GetAllAppsCommand, List<AppDetailResponse>>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));
    private readonly ICapabilityBridge _capabilityBridge = capabilityBridge ?? throw new ArgumentNullException(nameof(capabilityBridge));

    public async Task<CommandResult<List<AppDetailResponse>>> HandleAsync(GetAllAppsCommand command, CancellationToken ct = default)
    {
        var appRows = await _db.Database
            .SqlQuery<AppWithTypeRow>(
                $"""
                SELECT
                    A.[Id]
                    ,A.[ExternalId]
                    ,A.[Name]
                    ,A.[DisplayName]
                    ,A.[RegisteredAt]
                    ,A.[AppTypeId]
                    ,AT.[ExternalId] AS [AppTypeExternalId]
                    ,AT.[Name] AS [AppTypeName]
                    ,AT.[DisplayName] AS [AppTypeDisplayName]
                FROM
                    [App] A
                    INNER JOIN [AppType] AT ON AT.[Id] = A.[AppTypeId]
                ORDER BY
                    A.[Name]
                """)
            .ToListAsync(ct);

        var results = new List<AppDetailResponse>();

        foreach (var row in appRows)
        {
            var resolvedCapabilities = await _capabilityBridge.ResolveAllCapabilitiesAsync
            (
                row.Id, row.AppTypeId, ct
            );

            var routingConfiguration = _capabilityBridge.ExtractRoutingConfiguration(resolvedCapabilities);
            var hasProcessCapability = resolvedCapabilities.Exists
            (
                c => string.Equals(c.Slug, StringCatalog.Capabilities.Process, StringComparison.Ordinal)
            );

            var managedProcess = hasProcessCapability ? _supervisor.GetProcess(row.Id) : null;

            var runtime = new RuntimeState
            (
                hasProcessCapability ? RuntimeStateBuilder.BuildProcessState(managedProcess) : null,
                RuntimeStateBuilder.BuildRouteState(row.Name, routingConfiguration, _proxyConfigManager)
            );

            var capabilities = RuntimeStateBuilder.BuildCapabilityDictionary(resolvedCapabilities);

            var appTypeReference = new AppTypeReference
            (
                row.AppTypeExternalId,
                row.AppTypeName,
                row.AppTypeDisplayName
            );

            results.Add(new AppDetailResponse
            (
                row.ExternalId,
                row.Name,
                row.DisplayName,
                appTypeReference,
                row.RegisteredAt,
                runtime,
                capabilities
            ));
        }

        return CommandResult<List<AppDetailResponse>>.Success(results);
    }
}
#pragma warning restore MA0051

internal sealed record AppWithTypeRow
(
    Guid Id,
    string ExternalId,
    string Name,
    string DisplayName,
    DateTime RegisteredAt,
    Guid AppTypeId,
    string AppTypeExternalId,
    string AppTypeName,
    string AppTypeDisplayName
);
