using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Features.AppTypes;

public static class Update
{
    public record Request
    (
        string DisplayName,
        string? Description,
        Dictionary<string, JsonObject?>? Capabilities
    );

    public static async Task<Results<NoContent, NotFound, ProblemHttpResult>> HandleAsync
    (
        string externalId,
        Request request,
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var command = new UpdateAppTypeCommand
        (
            externalId,
            request.DisplayName,
            request.Description,
            request.Capabilities
        );

        var result = await dispatcher.DispatchAsync(command, cancellationToken);

        return result.IsSuccess
            ? (Results<NoContent, NotFound, ProblemHttpResult>)TypedResults.NoContent()
            : result.ErrorCode == "NOT_FOUND"
            ? TypedResults.NotFound()
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}

public record UpdateAppTypeCommand
(
    string ExternalId,
    string DisplayName,
    string? Description,
    Dictionary<string, JsonObject?>? Capabilities
) : ICommand<Empty>;

#pragma warning disable MA0051 // Long method justified — update with capability synchronization
public sealed class UpdateAppTypeCommandHandler(CollabhostDbContext db)
    : ICommandHandler<UpdateAppTypeCommand, Empty>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<Empty>> HandleAsync
    (
        UpdateAppTypeCommand command,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return CommandResult<Empty>.Fail("INVALID_DISPLAY_NAME", "Display name is required.");
        }

        var appType = await _db.Set<AppType>()
            .SingleOrDefaultAsync(t => t.ExternalId == command.ExternalId, ct);

        if (appType is null)
        {
            return CommandResult<Empty>.Fail("NOT_FOUND", "App type not found.");
        }

        appType.UpdateDetails(command.DisplayName, command.Description);

        if (command.Capabilities is not null)
        {
            var existingCapabilities = await _db.Set<AppTypeCapability>()
                .Where(atc => atc.AppTypeId == appType.Id)
                .ToListAsync(ct);

            foreach (var (slug, configJson) in command.Capabilities)
            {
                var capability = await _db.Set<Capability>()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(c => c.Slug == slug, ct);

                if (capability is null)
                {
                    return CommandResult<Empty>.Fail("UNKNOWN_CAPABILITY", $"Unknown capability slug: '{slug}'.");
                }

                var existing = existingCapabilities
                    .SingleOrDefault(e => e.CapabilityId == capability.Id);

                if (configJson is null)
                {
                    // Null means remove the capability from this type
                    if (existing is not null)
                    {
                        _db.Set<AppTypeCapability>().Remove(existing);
                    }
                }
                else
                {
                    var validationError = CapabilityConfigurationValidator.Validate(slug, configJson);
                    if (validationError is not null)
                    {
                        return CommandResult<Empty>.Fail("INVALID_CONFIGURATION", validationError);
                    }

                    var configString = configJson.ToJsonString();

                    if (existing is not null)
                    {
                        existing.UpdateConfiguration(configString);
                    }
                    else
                    {
                        var appTypeCapability = AppTypeCapability.Create
                        (
                            appType.Id,
                            capability.Id,
                            configString
                        );

                        _db.Set<AppTypeCapability>().Add(appTypeCapability);
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        return CommandResult<Empty>.Success(Empty.Value);
    }
}
#pragma warning restore MA0051
