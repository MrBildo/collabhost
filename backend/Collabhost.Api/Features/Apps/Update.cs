using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Features.AppTypes;

namespace Collabhost.Api.Features.Apps;

public static class Update
{
    public record Request
    (
        string? DisplayName,
        IDictionary<string, JsonObject?>? CapabilityOverrides
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
            request.CapabilityOverrides
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
    string? DisplayName,
    IDictionary<string, JsonObject?>? CapabilityOverrides
) : ICommand<Empty>;

#pragma warning disable MA0051 // Long method justified — app update with capability override synchronization
public sealed class UpdateAppCommandHandler
(
    CollabhostDbContext db,
    ProxyConfigManager proxyConfigManager
) : ICommandHandler<UpdateAppCommand, Empty>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

    public async Task<CommandResult<Empty>> HandleAsync(UpdateAppCommand command, CancellationToken ct = default)
    {
        var app = await _db.Apps
            .SingleOrDefaultAsync(a => a.ExternalId == command.ExternalId, ct);
        if (app is null)
        {
            return CommandResult<Empty>.Fail("NOT_FOUND", "App not found.");
        }

        if (command.DisplayName is not null)
        {
            if (string.IsNullOrWhiteSpace(command.DisplayName))
            {
                return CommandResult<Empty>.Fail("INVALID_DISPLAY_NAME", "Display name cannot be empty.");
            }

            app.UpdateDetails(command.DisplayName);
        }

        if (command.CapabilityOverrides is not null)
        {
            var typeCapabilities = await _db.Set<AppTypeCapability>()
                .AsNoTracking()
                .Where(atc => atc.AppTypeId == app.AppTypeId)
                .ToListAsync(ct);

            var capabilities = await _db.Set<Capability>()
                .AsNoTracking()
                .ToListAsync(ct);

            var capabilityLookup = capabilities.ToDictionary(c => c.Slug, StringComparer.Ordinal);

            var existingOverrides = await _db.Set<CapabilityConfiguration>()
                .Where(cc => cc.AppId == app.Id)
                .ToListAsync(ct);

            foreach (var (overrideSlug, overrideJson) in command.CapabilityOverrides)
            {
                if (!capabilityLookup.TryGetValue(overrideSlug, out var capability))
                {
                    return CommandResult<Empty>.Fail("UNKNOWN_CAPABILITY", $"Unknown capability slug: '{overrideSlug}'.");
                }

                var typeCapability = typeCapabilities
                    .SingleOrDefault(tc => tc.CapabilityId == capability.Id);

                if (typeCapability is null)
                {
                    return CommandResult<Empty>.Fail("INVALID_OVERRIDE", $"This app's type does not have capability '{overrideSlug}'.");
                }

                var existingOverride = existingOverrides
                    .SingleOrDefault(e => e.AppTypeCapabilityId == typeCapability.Id);

                if (overrideJson is null || overrideJson.IsEmptyObject())
                {
                    // Null or empty = delete override (reset to type defaults)
                    if (existingOverride is not null)
                    {
                        _db.Set<CapabilityConfiguration>().Remove(existingOverride);
                    }
                }
                else
                {
                    var validationError = CapabilityConfigurationValidator.Validate(overrideSlug, overrideJson);
                    if (validationError is not null)
                    {
                        return CommandResult<Empty>.Fail("INVALID_CONFIGURATION", validationError);
                    }

                    var configString = overrideJson.ToJsonString();

                    if (existingOverride is not null)
                    {
                        existingOverride.UpdateConfiguration(configString);
                    }
                    else
                    {
                        var capabilityConfiguration = CapabilityConfiguration.Create
                        (
                            app.Id,
                            typeCapability.Id,
                            configString
                        );

                        _db.Set<CapabilityConfiguration>().Add(capabilityConfiguration);
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        await _proxyConfigManager.SyncRoutesAsync(ct);

        return CommandResult<Empty>.Success(Empty.Value);
    }
}
#pragma warning restore MA0051
