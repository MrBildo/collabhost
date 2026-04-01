using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.Apps;

public static class Start
{
    public static async Task<Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new StartCommand(externalId), ct);

        Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            { ErrorCode: "ALREADY_RUNNING" } => TypedResults.Problem(result.ErrorMessage, statusCode: 409),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record StartCommand(string ExternalId) : ICommand<AppDetailResponse>;

#pragma warning disable MA0051 // Long method justified — bridge orchestration across process and proxy systems
public sealed class StartCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    ProxyConfigManager proxyConfigManager,
    ICapabilityBridge capabilityBridge,
    IProcessStateNameResolver stateNameResolver
) : ICommandHandler<StartCommand, AppDetailResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));
    private readonly ICapabilityBridge _capabilityBridge = capabilityBridge ?? throw new ArgumentNullException(nameof(capabilityBridge));
    private readonly IProcessStateNameResolver _stateNameResolver = stateNameResolver ?? throw new ArgumentNullException(nameof(stateNameResolver));

    public async Task<CommandResult<AppDetailResponse>> HandleAsync(StartCommand command, CancellationToken ct = default)
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

        var hasRouting = resolvedCapabilities.HasCapability(StringCatalog.Capabilities.Routing);
        var hasProcess = resolvedCapabilities.HasCapability(StringCatalog.Capabilities.Process);

        // Bridge orchestration: enable route first, then start process
        if (hasRouting)
        {
            _proxyConfigManager.EnableRoute(app.Name);
            await _proxyConfigManager.SyncRoutesAsync(ct);
        }

        ManagedProcess? managedProcess = null;

        if (hasProcess)
        {
            try
            {
                managedProcess = await _supervisor.StartAppAsync(app.Id, ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already running", StringComparison.Ordinal))
            {
                return CommandResult<AppDetailResponse>.Fail("ALREADY_RUNNING", ex.Message);
            }
        }

        // Build the bridge response
        return await BuildAppDetailResponseAsync(command.ExternalId, app, resolvedCapabilities, managedProcess, hasProcess, ct);
    }

    private async Task<CommandResult<AppDetailResponse>> BuildAppDetailResponseAsync
    (
        string externalId,
        AppLookup app,
        IReadOnlyList<ResolvedCapabilityData> resolvedCapabilities,
        ManagedProcess? managedProcess,
        bool hasProcess,
        CancellationToken ct
    )
    {
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
                    A.[ExternalId] = {externalId}
                """
            )
            .SingleAsync(ct);

        var runtime = new RuntimeState
        (
            hasProcess
                ? await RuntimeStateBuilder.BuildProcessStateAsync(managedProcess, _stateNameResolver, ct)
                : null,
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
