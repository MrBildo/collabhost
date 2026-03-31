using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.Apps;

public static class Stop
{
    public static async Task<Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new StopCommand(externalId), ct);

        Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "ALREADY_STOPPED" } => TypedResults.Problem(result.ErrorMessage, statusCode: 409),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record StopCommand(string ExternalId) : ICommand<AppDetailResponse>;

#pragma warning disable MA0051 // Long method justified — bridge orchestration across process and proxy systems
public sealed class StopCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    ProxyConfigManager proxyConfigManager,
    ICapabilityBridge capabilityBridge
) : ICommandHandler<StopCommand, AppDetailResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));
    private readonly ICapabilityBridge _capabilityBridge = capabilityBridge ?? throw new ArgumentNullException(nameof(capabilityBridge));

    public async Task<CommandResult<AppDetailResponse>> HandleAsync(StopCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<AppDetailResponse>.Fail("NOT_FOUND", "App not found.");
        }

        var resolvedCapabilities = await _capabilityBridge.ResolveAllCapabilitiesAsync
        (
            app.Id, app.AppTypeId, ct
        );

        var hasProcess = resolvedCapabilities.Exists
        (
            c => string.Equals(c.Slug, StringCatalog.Capabilities.Process, StringComparison.Ordinal)
        );
        var hasRouting = resolvedCapabilities.Exists
        (
            c => string.Equals(c.Slug, StringCatalog.Capabilities.Routing, StringComparison.Ordinal)
        );

        // Bridge orchestration: stop process first, then disable route
        ManagedProcess? managedProcess = null;

        if (hasProcess)
        {
            try
            {
                managedProcess = await _supervisor.StopAppAsync(app.Id, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already stopped", StringComparison.Ordinal))
            {
                return CommandResult<AppDetailResponse>.Fail("ALREADY_STOPPED", ex.Message);
            }
        }

        if (hasRouting)
        {
            _proxyConfigManager.DisableRoute(app.Name);
            await _proxyConfigManager.SyncRoutesAsync(ct);
        }

        // Build the bridge response
        var routingConfiguration = _capabilityBridge.ExtractRoutingConfiguration(resolvedCapabilities);

        managedProcess ??= hasProcess ? _supervisor.GetProcess(app.Id) : null;

        var appRow = await _db.Database
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
            .SingleAsync(ct);

        var runtime = new RuntimeState
        (
            hasProcess ? RuntimeStateBuilder.BuildProcessState(managedProcess) : null,
            RuntimeStateBuilder.BuildRouteState(appRow.Name, routingConfiguration, _proxyConfigManager)
        );

        var capabilities = RuntimeStateBuilder.BuildCapabilityDictionary(resolvedCapabilities);

        var appTypeReference = new AppTypeReference
        (
            appRow.AppTypeExternalId,
            appRow.AppTypeName,
            appRow.AppTypeDisplayName
        );

        var response = new AppDetailResponse
        (
            appRow.ExternalId,
            appRow.Name,
            appRow.DisplayName,
            appTypeReference,
            appRow.RegisteredAt,
            runtime,
            capabilities
        );

        return CommandResult<AppDetailResponse>.Success(response);
    }
}
#pragma warning restore MA0051
