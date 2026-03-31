using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.AppTypes;

public static class Create
{
    public record Request
    (
        string Name,
        string DisplayName,
        string? Description,
        Dictionary<string, JsonObject>? Capabilities
    );

    public record Response(string ExternalId);

    public static async Task<Results<Created<Response>, ProblemHttpResult>> HandleAsync
    (
        Request request,
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var command = new CreateAppTypeCommand
        (
            request.Name,
            request.DisplayName,
            request.Description,
            request.Capabilities
        );

        var result = await dispatcher.DispatchAsync(command, cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/app-types/{result.Value}", new Response(result.Value!))
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}

public record CreateAppTypeCommand
(
    string Name,
    string DisplayName,
    string? Description,
    Dictionary<string, JsonObject>? Capabilities
) : ICommand<string>;

public sealed class CreateAppTypeCommandHandler(CollabhostDbContext db)
    : ICommandHandler<CreateAppTypeCommand, string>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<string>> HandleAsync
    (
        CreateAppTypeCommand command,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return CommandResult<string>.Fail("INVALID_NAME", "Name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return CommandResult<string>.Fail("INVALID_DISPLAY_NAME", "Display name is required.");
        }

        var normalizedName = command.Name.Trim().ToLowerInvariant();

        var nameExists = await _db.Set<AppType>()
            .AnyAsync
            (
                t => t.Name == normalizedName, ct
            );

        if (nameExists)
        {
            return CommandResult<string>.Fail("DUPLICATE_NAME", $"An app type with the name '{normalizedName}' already exists.");
        }

        var appType = AppType.Create
        (
            command.Name,
            command.DisplayName,
            command.Description,
            isBuiltIn: false
        );

        _db.Set<AppType>().Add(appType);

        if (command.Capabilities is not null)
        {
            var validationResult = await CreateCapabilityRowsAsync(appType.Id, command.Capabilities, ct);
            if (!validationResult.IsSuccess)
            {
                return CommandResult<string>.Fail(validationResult.ErrorCode, validationResult.ErrorMessage);
            }
        }

        await _db.SaveChangesAsync(ct);

        return CommandResult<string>.Success(appType.ExternalId);
    }

    private async Task<CommandResult> CreateCapabilityRowsAsync
    (
        Guid appTypeId,
        Dictionary<string, JsonObject> capabilities,
        CancellationToken ct
    )
    {
        foreach (var (slug, configJson) in capabilities)
        {
            var capability = await _db.Set<Capability>()
                .AsNoTracking()
                .SingleOrDefaultAsync
                (
                    c => c.Slug == slug, ct
                );

            if (capability is null)
            {
                return CommandResult.Fail("UNKNOWN_CAPABILITY", $"Unknown capability slug: '{slug}'.");
            }

            var validationError = CapabilityConfigurationValidator.Validate(slug, configJson);
            if (validationError is not null)
            {
                return CommandResult.Fail("INVALID_CONFIGURATION", validationError);
            }

            var appTypeCapability = AppTypeCapability.Create
            (
                appTypeId,
                capability.Id,
                configJson.ToJsonString()
            );

            _db.Set<AppTypeCapability>().Add(appTypeCapability);
        }

        return CommandResult.Success();
    }
}
