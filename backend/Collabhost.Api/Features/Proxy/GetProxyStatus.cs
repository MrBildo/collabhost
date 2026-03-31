namespace Collabhost.Api.Features.Proxy;

public static class GetProxyStatus
{
    public record ProxyAppRow(Guid Id, string ExternalId);

    public static async Task<Results<Ok<ProxyStatusResponse>, ProblemHttpResult>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var result = await dispatcher.DispatchAsync(new GetProxyStatusCommand(), ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(result.ErrorMessage, statusCode: 500);
    }
}

public record GetProxyStatusCommand : ICommand<ProxyStatusResponse>;

public class GetProxyStatusCommandHandler
(
    CollabhostDbContext db,
    ProcessSupervisor supervisor,
    IProxyConfigClient proxyClient,
    ProxySettings settings
) : ICommandHandler<GetProxyStatusCommand, ProxyStatusResponse>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProcessSupervisor _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
    private readonly IProxyConfigClient _proxyClient = proxyClient ?? throw new ArgumentNullException(nameof(proxyClient));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

#pragma warning disable MA0051 // Long method justified — multi-step proxy status aggregation
    public async Task<CommandResult<ProxyStatusResponse>> HandleAsync(GetProxyStatusCommand command, CancellationToken ct = default)
    {
        // Find the proxy app by name
        var proxyApp = await _db.Database
            .SqlQuery<GetProxyStatus.ProxyAppRow>
            (
                $"""
                SELECT
                    A.[Id]
                    ,A.[ExternalId]
                FROM
                    [App] A
                WHERE
                    A.[Name] = 'proxy'
                """
            )
            .SingleOrDefaultAsync(ct);

        // Determine process state
        string state;
        int? pid = null;

        if (proxyApp is not null)
        {
            var managed = _supervisor.GetProcess(proxyApp.Id);
            if (managed is not null)
            {
                state = managed.IsRunning ? "Running"
                    : managed.IsCrashed ? "Crashed"
                    : managed.IsRestarting ? "Restarting"
                    : "Stopped";
                pid = managed.Pid;
            }
            else
            {
                state = "Stopped";
            }
        }
        else
        {
            state = "NotRegistered";
        }

        // Check admin API readiness
        var adminApiReady = await _proxyClient.IsReadyAsync(ct);

        // Count routable apps (apps with the routing capability)
        var routeCount = await _db.Database
            .SqlQuery<int>
            (
                $"""
                SELECT
                    COUNT(*) AS [Value]
                FROM
                    [App] A
                    INNER JOIN [AppTypeCapability] ATC ON ATC.[AppTypeId] = A.[AppTypeId]
                    INNER JOIN [Capability] C ON C.[Id] = ATC.[CapabilityId]
                WHERE
                    C.[Slug] = 'routing'
                """
            )
            .SingleAsync(ct);

        return CommandResult<ProxyStatusResponse>.Success
        (
            new ProxyStatusResponse
            (
                state,
                pid,
                adminApiReady,
                routeCount,
                _settings.BaseDomain
            )
        );
    }
#pragma warning restore MA0051
}
