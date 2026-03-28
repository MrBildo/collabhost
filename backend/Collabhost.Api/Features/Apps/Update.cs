using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain.Lookups;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class Update
{
    public record Request
    (
        string DisplayName,
        string InstallDirectory,
        string CommandLine,
        string? Arguments,
        string? WorkingDirectory,
        Guid RestartPolicyId,
        string? HealthEndpoint,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart
    );

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        Request request,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var command = new UpdateAppCommand
        (
            externalId,
            request.DisplayName,
            request.InstallDirectory,
            request.CommandLine,
            request.Arguments,
            request.WorkingDirectory,
            request.RestartPolicyId,
            request.HealthEndpoint,
            request.UpdateCommand,
            request.UpdateTimeoutSeconds,
            request.AutoStart
        );

        var result = await dispatcher.DispatchAsync(command, ct);

        return result.IsSuccess
            ? (Results<NoContent, NotFound, ProblemHttpResult>)TypedResults.NoContent()
            : result.ErrorCode == "NOT_FOUND"
            ? TypedResults.NotFound()
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}

public record UpdateAppCommand
(
    string ExternalId,
    string DisplayName,
    string InstallDirectory,
    string CommandLine,
    string? Arguments,
    string? WorkingDirectory,
    Guid RestartPolicyId,
    string? HealthEndpoint,
    string? UpdateCommand,
    int? UpdateTimeoutSeconds,
    bool AutoStart
) : ICommand<Empty>;

public class UpdateAppCommandHandler
(
    CollabhostDbContext db,
    ProxyConfigManager proxyConfigManager
) : ICommandHandler<UpdateAppCommand, Empty>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

    public async Task<CommandResult<Empty>> HandleAsync(UpdateAppCommand command, CancellationToken ct = default)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return CommandResult<Empty>.Fail("INVALID_DISPLAY_NAME", "Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.InstallDirectory))
        {
            return CommandResult<Empty>.Fail("INVALID_INSTALL_DIRECTORY", "Install directory is required.");
        }

        if (string.IsNullOrWhiteSpace(command.CommandLine))
        {
            return CommandResult<Empty>.Fail("INVALID_COMMAND_LINE", "CreateCommand line is required.");
        }

        var app = await _db.Apps.SingleOrDefaultAsync(a => a.ExternalId == command.ExternalId, ct);
        if (app is null)
        {
            return CommandResult<Empty>.Fail("NOT_FOUND", "App not found.");
        }

        // Validate lookup reference
        var restartPolicyExists = await _db.Set<RestartPolicy>().AnyAsync(p => p.Id == command.RestartPolicyId, ct);
        if (!restartPolicyExists)
        {
            return CommandResult<Empty>.Fail("INVALID_RESTART_POLICY", "The specified RestartPolicyId does not exist.");
        }

        app.UpdateConfiguration
        (
            command.DisplayName,
            command.InstallDirectory,
            command.CommandLine,
            command.Arguments,
            command.WorkingDirectory,
            command.RestartPolicyId,
            command.HealthEndpoint,
            command.UpdateCommand,
            command.UpdateTimeoutSeconds,
            command.AutoStart
        );

        await _db.SaveChangesAsync(ct);

        _ = _proxyConfigManager.SyncRoutesAsync();

        return CommandResult<Empty>.Success(Empty.Value);
    }
}
