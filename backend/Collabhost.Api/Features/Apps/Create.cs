using System.Text.Json;
using System.Text.Json.Nodes;

using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Values;
using Collabhost.Api.Features.AppTypes;

namespace Collabhost.Api.Features.Apps;

public static class Create
{
    public record Request
    (
        string Name,
        string DisplayName,
        string AppTypeId,
        Dictionary<string, JsonObject?>? CapabilityOverrides
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
            request.CapabilityOverrides
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
    string AppTypeExternalId,
    Dictionary<string, JsonObject?>? CapabilityOverrides
) : ICommand<string>;

#pragma warning disable MA0051 // Long method justified — app creation with capability override validation
public sealed class CreateCommandHandler
(
    CollabhostDbContext db,
    ProxyConfigManager proxyConfigManager
) : ICommandHandler<CreateCommand, string>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxyConfigManager _proxyConfigManager = proxyConfigManager ?? throw new ArgumentNullException(nameof(proxyConfigManager));

    public async Task<CommandResult<string>> HandleAsync(CreateCommand command, CancellationToken ct = default)
    {
        var (slugValid, slugError) = AppSlugValue.CanCreate(command.Name);
        if (!slugValid)
        {
            return CommandResult<string>.Fail("INVALID_NAME", slugError);
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName))
        {
            return CommandResult<string>.Fail("INVALID_DISPLAY_NAME", "Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(command.AppTypeExternalId))
        {
            return CommandResult<string>.Fail("INVALID_APP_TYPE", "App type ID is required.");
        }

        // Look up app type by ExternalId
        var appType = await _db.Set<AppType>()
            .AsNoTracking()
            .SingleOrDefaultAsync
            (
                t => t.ExternalId == command.AppTypeExternalId, ct
            );

        if (appType is null)
        {
            return CommandResult<string>.Fail("INVALID_APP_TYPE", "The specified app type does not exist.");
        }

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
            appType.Id
        );

        _db.Apps.Add(app);

        // Process capability overrides
        if (command.CapabilityOverrides is not null)
        {
            var typeCapabilities = await _db.Set<AppTypeCapability>()
                .AsNoTracking()
                .Where(atc => atc.AppTypeId == appType.Id)
                .ToListAsync(ct);

            var capabilities = await _db.Set<Capability>()
                .AsNoTracking()
                .ToListAsync(ct);

            var capabilityLookup = capabilities.ToDictionary(c => c.Slug, StringComparer.Ordinal);

            foreach (var (overrideSlug, overrideJson) in command.CapabilityOverrides)
            {
                if (overrideJson is null || overrideJson.IsEmptyObject())
                {
                    continue;
                }

                if (!capabilityLookup.TryGetValue(overrideSlug, out var capability))
                {
                    return CommandResult<string>.Fail("UNKNOWN_CAPABILITY", $"Unknown capability slug: '{overrideSlug}'.");
                }

                var typeCapability = typeCapabilities
                    .SingleOrDefault(tc => tc.CapabilityId == capability.Id);

                if (typeCapability is null)
                {
                    return CommandResult<string>.Fail("INVALID_OVERRIDE", $"App type '{appType.Name}' does not have capability '{overrideSlug}'.");
                }

                var validationError = CapabilityConfigurationValidator.Validate(overrideSlug, overrideJson);
                if (validationError is not null)
                {
                    return CommandResult<string>.Fail("INVALID_CONFIGURATION", validationError);
                }

                var capabilityConfiguration = CapabilityConfiguration.Create
                (
                    app.Id,
                    typeCapability.Id,
                    overrideJson.ToJsonString()
                );

                _db.Set<CapabilityConfiguration>().Add(capabilityConfiguration);
            }
        }

        await _db.SaveChangesAsync(ct);

        await _proxyConfigManager.SyncRoutesAsync(ct);

        return CommandResult<string>.Success(app.ExternalId);
    }
}
#pragma warning restore MA0051

