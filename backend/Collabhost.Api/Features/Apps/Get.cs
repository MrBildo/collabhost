using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.Apps;

public static class Get
{
    public static async Task<Results<Ok<AppDetailResponse>, NotFound>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetAppCommand(externalId), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.NotFound();
    }
}

public record GetAppCommand(string ExternalId) : ICommand<AppDetailResponse>;

#pragma warning disable MA0051 // Long method justified — bridge aggregation across DB and core systems
public sealed class GetAppCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    ProxyConfigManager proxyConfigManager,
    ICapabilityBridge capabilityBridge,
    IProcessStateNameResolver stateNameResolver
) : ICommandHandler<GetAppCommand, AppDetailResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));
    private readonly ICapabilityBridge _capabilityBridge = capabilityBridge ?? throw new ArgumentNullException(nameof(capabilityBridge));
    private readonly IProcessStateNameResolver _stateNameResolver = stateNameResolver ?? throw new ArgumentNullException(nameof(stateNameResolver));

    public async Task<CommandResult<AppDetailResponse>> HandleAsync(GetAppCommand command, CancellationToken ct = default)
    {
        var row = await _db.Database
            .SqlQuery<AppWithTypeRow>
            (
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
                WHERE
                    A.[ExternalId] = {command.ExternalId}
                """
            )
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return CommandResult<AppDetailResponse>.Fail("NOT_FOUND", "App not found.");
        }

        var resolvedCapabilities = await _capabilityBridge.ResolveAllCapabilitiesAsync
        (
            row.Id, row.AppTypeId, ct
        );

        var routingConfiguration = _capabilityBridge.ExtractRoutingConfiguration(resolvedCapabilities);
        var hasProcessCapability = resolvedCapabilities.HasCapability(StringCatalog.Capabilities.Process);

        var managedProcess = hasProcessCapability ? _supervisor.GetProcess(row.Id) : null;

        var runtime = new RuntimeState
        (
            hasProcessCapability
                ? await RuntimeStateBuilder.BuildProcessStateAsync(managedProcess, _stateNameResolver, ct)
                : null,
            RuntimeStateBuilder.BuildRouteState(row.Name, routingConfiguration, _proxyConfigManager)
        );

        var capabilities = RuntimeStateBuilder.BuildCapabilityDictionary(resolvedCapabilities);

        var appTypeReference = new AppTypeReference
        (
            row.AppTypeExternalId,
            row.AppTypeName,
            row.AppTypeDisplayName
        );

        var response = new AppDetailResponse
        (
            row.ExternalId,
            row.Name,
            row.DisplayName,
            appTypeReference,
            row.RegisteredAt,
            runtime,
            capabilities
        );

        return CommandResult<AppDetailResponse>.Success(response);
    }
}
#pragma warning restore MA0051
