using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Lookups;
using Collabhost.Api.Domain.Values;

namespace Collabhost.Api.Features.Apps;

public static class Create
{
    public record Request
    (
        string Name,
        string DisplayName,
        Guid AppTypeId,
        string InstallDirectory,
        string CommandLine,
        string? Arguments,
        string? WorkingDirectory,
        Guid? RestartPolicyId,
        string? HealthEndpoint,
        string? UpdateCommand,
        int? UpdateTimeoutSeconds,
        bool AutoStart
    );

    public record Response(string ExternalId);

    public static async Task<Results<Created<Response>, ProblemHttpResult>> HandleAsync
    (
        Request request,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var command = new CreateCommand
        (
            request.Name,
            request.DisplayName,
            request.AppTypeId,
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
            ? TypedResults.Created($"/api/v1/apps/{result.Value}", new Response(result.Value!))
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}

public record CreateCommand
(
    string Name,
    string DisplayName,
    Guid AppTypeId,
    string InstallDirectory,
    string CommandLine,
    string? Arguments,
    string? WorkingDirectory,
    Guid? RestartPolicyId,
    string? HealthEndpoint,
    string? UpdateCommand,
    int? UpdateTimeoutSeconds,
    bool AutoStart
) : ICommand<string>;

public class CreateCommandHandler
(
    CollabhostDbContext db,
    PortAllocator portAllocator,
    ProxyConfigManager proxyConfigManager
) : ICommandHandler<CreateCommand, string>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly PortAllocator _portAllocator = portAllocator ?? throw new ArgumentNullException(nameof(portAllocator));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

    public async Task<CommandResult<string>> HandleAsync(CreateCommand command, CancellationToken ct = default)
    {
        // Validate required fields
        var (slugValid, slugError) = AppSlugValue.CanCreate(command.Name);
        if (!slugValid)
        {
            return CommandResult<string>.Fail("INVALID_NAME", slugError);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return CommandResult<string>.Fail("INVALID_DISPLAY_NAME", "Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.InstallDirectory))
        {
            return CommandResult<string>.Fail("INVALID_INSTALL_DIRECTORY", "Install directory is required.");
        }

        // Validate lookup references exist
        var appTypeExists = await _db.Set<AppType>().AnyAsync(t => t.Id == command.AppTypeId, ct);
        if (!appTypeExists)
        {
            return CommandResult<string>.Fail("INVALID_APP_TYPE", "The specified AppTypeId does not exist.");
        }

        var isStaticSite = command.AppTypeId == Domain.Catalogs.IdentifierCatalog.AppTypes.StaticSite;
        if (!isStaticSite && string.IsNullOrWhiteSpace(command.CommandLine))
        {
            return CommandResult<string>.Fail("INVALID_COMMAND_LINE", "Command line is required for non-static-site app types.");
        }

        // Static sites have no process — force "Never" restart policy and disable auto-start
        var restartPolicyId = isStaticSite
            ? Domain.Catalogs.IdentifierCatalog.RestartPolicies.Never
            : command.RestartPolicyId ?? Domain.Catalogs.IdentifierCatalog.RestartPolicies.Never;
        var autoStart = isStaticSite ? false : command.AutoStart;

        var restartPolicyExists = await _db.Set<RestartPolicy>().AnyAsync(p => p.Id == restartPolicyId, ct);
        if (!restartPolicyExists)
        {
            return CommandResult<string>.Fail("INVALID_RESTART_POLICY", "The specified RestartPolicyId does not exist.");
        }

        // Check for duplicate name
        var slug = AppSlugValue.Create(command.Name);
        var nameExists = await _db.Apps.AnyAsync(a => a.Name == slug, ct);
        if (nameExists)
        {
            return CommandResult<string>.Fail("DUPLICATE_NAME", $"An app with the name '{slug.Value}' already exists.");
        }

        var app = App.Register
        (
            slug,
            command.DisplayName,
            command.AppTypeId,
            command.InstallDirectory,
            command.CommandLine,
            command.Arguments,
            command.WorkingDirectory,
            restartPolicyId,
            command.HealthEndpoint,
            command.UpdateCommand,
            command.UpdateTimeoutSeconds,
            autoStart
        );

        // Auto-assign port
        var port = await _portAllocator.AllocateAsync(ct);
        app.AssignPort(port);

        _db.Apps.Add(app);
        await _db.SaveChangesAsync(ct);

        if (AppTypeBehavior.IsRoutable(command.AppTypeId))
        {
            _ = _proxyConfigManager.SyncRoutesAsync(ct);
        }

        return CommandResult<string>.Success(app.ExternalId);
    }
}
