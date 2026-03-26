using Collabhost.Api.Common;
using Collabhost.Api.Data;
using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Lookups;
using Collabhost.Api.Domain.Values;
using Collabhost.Api.Services;

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
        Guid RestartPolicyId,
        string? HealthEndpoint,
        string? UpdateCommand,
        bool AutoStart
    );

    public record Command
    (
        string Name,
        string DisplayName,
        Guid AppTypeId,
        string InstallDirectory,
        string CommandLine,
        string? Arguments,
        string? WorkingDirectory,
        Guid RestartPolicyId,
        string? HealthEndpoint,
        string? UpdateCommand,
        bool AutoStart
    );

    public record Response(string ExternalId);

    public class Handler
    (
        CollabhostDbContext db,
        PortAllocator portAllocator
    )
    {
        private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
        private readonly PortAllocator _portAllocator = portAllocator ?? throw new ArgumentNullException(nameof(portAllocator));

        public async Task<CommandResult<string>> HandleAsync(Command command, CancellationToken ct = default)
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

            if (string.IsNullOrWhiteSpace(command.CommandLine))
            {
                return CommandResult<string>.Fail("INVALID_COMMAND_LINE", "Command line is required.");
            }

            // Validate lookup references exist
            var appTypeExists = await _db.Set<AppType>().AnyAsync(t => t.Id == command.AppTypeId, ct);
            if (!appTypeExists)
            {
                return CommandResult<string>.Fail("INVALID_APP_TYPE", "The specified AppTypeId does not exist.");
            }

            var restartPolicyExists = await _db.Set<RestartPolicy>().AnyAsync(p => p.Id == command.RestartPolicyId, ct);
            if (!restartPolicyExists)
            {
                return CommandResult<string>.Fail("INVALID_RESTART_POLICY", "The specified RestartPolicyId does not exist.");
            }

            // Check for duplicate name
            var slug = AppSlugValue.Create(command.Name);
            var nameExists = await _db.Apps.AnyAsync(a => a.Name == slug.Value, ct);
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
                command.RestartPolicyId,
                command.HealthEndpoint,
                command.UpdateCommand,
                command.AutoStart
            );

            // Auto-assign port
            var port = await _portAllocator.AllocateAsync(ct);
            app.AssignPort(port);

            _db.Apps.Add(app);
            await _db.SaveChangesAsync(ct);

            return CommandResult<string>.Success(app.ExternalId);
        }
    }

    public static async Task<Results<Created<Response>, ProblemHttpResult>> HandleAsync
    (
        Request request,
        Handler handler,
        CancellationToken ct
    )
    {
        var command = new Command
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
            request.AutoStart
        );

        var result = await handler.HandleAsync(command, ct);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/apps/{result.Value}", new Response(result.Value!))
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}
