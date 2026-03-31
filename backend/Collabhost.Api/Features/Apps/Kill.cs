using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.Apps;

public static class Kill
{
    public static async Task<Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new KillCommand(externalId), ct);

        Results<Ok<AppDetailResponse>, NotFound, ProblemHttpResult> response = result switch
        {
            { IsSuccess: true } => TypedResults.Ok(result.Value),
            { ErrorCode: "NOT_FOUND" } => TypedResults.NotFound(),
            _ => TypedResults.Problem(result.ErrorMessage, statusCode: 400)
        };

        return response;
    }
}

public record KillCommand(string ExternalId) : ICommand<AppDetailResponse>;

#pragma warning disable MA0051 // Long method justified — bridge kill with full response
public sealed class KillCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    ProxyConfigManager proxyConfigManager,
    ICapabilityBridge capabilityBridge
) : ICommandHandler<KillCommand, AppDetailResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));
    private readonly ICapabilityBridge _capabilityBridge = capabilityBridge ?? throw new ArgumentNullException(nameof(capabilityBridge));

    public async Task<CommandResult<AppDetailResponse>> HandleAsync(KillCommand command, CancellationToken ct = default)
    {
        var app = await _db.FindAppByExternalIdAsync(command.ExternalId, ct);

        if (app is null)
        {
            return CommandResult<AppDetailResponse>.Fail("NOT_FOUND", "App not found.");
        }

        var hasProcess = await _db.HasCapabilityAsync(app.AppTypeId, IdentifierCatalog.Capabilities.Process, ct);

        if (!hasProcess)
        {
            return CommandResult<AppDetailResponse>.Fail("NO_PROCESS", "This app type has no process to manage.");
        }

        var managedProcess = _supervisor.GetProcess(app.Id);

        if (managedProcess is null || !managedProcess.IsRunning)
        {
            return CommandResult<AppDetailResponse>.Fail("NOT_RUNNING", "No running process to kill.");
        }

        managedProcess.KillProcess();

        // Build the bridge response
        var resolvedCapabilities = await _capabilityBridge.ResolveAllCapabilitiesAsync
        (
            app.Id, app.AppTypeId, ct
        );

        var routingConfiguration = _capabilityBridge.ExtractRoutingConfiguration(resolvedCapabilities);

        // Re-read the process state after kill
        managedProcess = _supervisor.GetProcess(app.Id);

        var appRow = await _db.Database
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
                WHERE
                    A.[ExternalId] = {command.ExternalId}
                """)
            .SingleAsync(ct);

        var runtime = new RuntimeState
        (
            RuntimeStateBuilder.BuildProcessState(managedProcess),
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
