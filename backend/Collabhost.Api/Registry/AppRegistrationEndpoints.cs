using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Installation;
using Collabhost.Api.Proxy;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
internal static class AppRegistrationEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static async Task<IResult> CreateAppAsync
    (
        CreateAppRequest request,
        AppStore store,
        TypeStore typeStore,
        ProxyManager proxy,
        ProxySettings proxySettings,
        ExternalTargetSettings externalTargetSettings,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        AppDataPathResolver dataPathResolver,
        CancellationToken ct
    )
    {
        var (isValid, error) = Slug.Validate(request.Name);

        if (!isValid)
        {
            return TypedResults.Problem(error, statusCode: 400);
        }

        var exists = await store.ExistsBySlugAsync(request.Name, ct);

        if (exists)
        {
            return TypedResults.Problem
            (
                $"An app with slug '{request.Name}' already exists.",
                statusCode: 409
            );
        }

        var appType = typeStore.GetBySlug(request.AppTypeSlug);

        if (appType is null)
        {
            return TypedResults.Problem("App type not found.", statusCode: 404);
        }

        // Validate all settings BEFORE creating the app to ensure registration is transactional
        var validatedOverrides = new List<(string SectionKey, JsonObject Overrides)>();

        if (request.Values is not null)
        {
            // Collect process overrides from both the "process" section and the "discovery" virtual section
            JsonObject? processOverrides = null;

            foreach (var (sectionKey, sectionValues) in request.Values)
            {
                // The "discovery" section is a registration-only concept that maps to the "process" capability
                if (string.Equals(sectionKey, "discovery", StringComparison.Ordinal))
                {
                    processOverrides ??= [];

                    foreach (var (fieldKey, fieldValue) in sectionValues)
                    {
                        processOverrides[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
                    }

                    continue;
                }

                var overrideObject = new JsonObject();

                foreach (var (fieldKey, fieldValue) in sectionValues)
                {
                    overrideObject[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
                }

                var validationErrors = CapabilityResolver.ValidateEdits
                (
                    sectionKey, overrideObject, true, externalTargetSettings.AllowPublicHosts
                );

                if (validationErrors.Count > 0)
                {
                    return TypedResults.Problem
                    (
                        string.Join("; ", validationErrors),
                        statusCode: 400
                    );
                }

                // If this is the process section, merge with any discovery overrides
                if (string.Equals(sectionKey, "process", StringComparison.Ordinal))
                {
                    processOverrides ??= [];

                    foreach (var property in overrideObject)
                    {
                        processOverrides[property.Key] = property.Value?.DeepClone();
                    }

                    continue;
                }

                validatedOverrides.Add((sectionKey, overrideObject));
            }

            // Validate merged process overrides
            if (processOverrides is not null)
            {
                var processErrors = CapabilityResolver.ValidateEdits
                (
                    "process", processOverrides, true
                );

                if (processErrors.Count > 0)
                {
                    return TypedResults.Problem
                    (
                        string.Join("; ", processErrors),
                        statusCode: 400
                    );
                }

                validatedOverrides.Add(("process", processOverrides));
            }
        }

        // All validation passed -- now create the app and persist overrides
        var app = new App
        {
            Slug = request.Name.Trim().ToLowerInvariant(),
            DisplayName = request.DisplayName,
            AppTypeSlug = appType.Slug
        };

        await store.CreateAsync(app, ct);

        foreach (var (sectionKey, overrideObject) in validatedOverrides)
        {
            await store.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                overrideObject.ToJsonString(_jsonOptions),
                ct
            );
        }

        // Routing-only apps (e.g. static sites) start with their route disabled
        // because the operator still needs to populate an artifact directory
        // before the route is meaningful. External-route apps (Card #348, D8)
        // skip that intermediate step -- the upstream is operator-declared --
        // so they auto-enable at registration. Process-based apps have routes
        // tied to process state and need no explicit toggle here.
        var hasRouting = typeStore.HasBinding(appType.Slug, "routing");
        var hasProcess = typeStore.HasBinding(appType.Slug, "process");
        var hasExternalTarget = typeStore.HasBinding(appType.Slug, "external-target");

        if (hasRouting && !hasProcess)
        {
            if (hasExternalTarget)
            {
                proxy.EnableRoute(app.Slug);
                proxy.RequestSync();
            }
            else
            {
                proxy.DisableRoute(app.Slug);
            }
        }

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.AppCreated,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = app.Id.ToString(),
                AppSlug = app.Slug,
                MetadataJson = JsonSerializer.Serialize
                (
                    new { appTypeSlug = appType.Slug, displayName = app.DisplayName }
                )
            },
            ct
        );

        // Card #348 polish (C-1): external-route apps auto-enable at registration (D8).
        // Record AppStarted so the activity feed reflects the route going live -- the same
        // event the manual start path records for routing-only apps (StartAppAsync).
        if (hasExternalTarget)
        {
            await activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppStarted,
                    ActorId = currentUser.UserId.ToString(),
                    ActorName = currentUser.User.Name,
                    AppId = app.Id.ToString(),
                    AppSlug = app.Slug,
                    MetadataJson = null
                },
                ct
            );
        }

        // Card #345: surface a one-line hint so the operator knows about the new
        // `collabhost --update-hosts` CLI for resolving <slug>.<baseDomain> from a
        // browser on this host. Resolve the effective domain through the same
        // CapabilityResolver path the routing surface uses so an operator-set
        // DomainPattern is reflected in the hint.
        var hints = ResolveHelpfulNextSteps(appType.Slug, app.Slug, typeStore, validatedOverrides, proxySettings);

        return TypedResults.Created
        (
            $"/api/v1/apps/{app.Slug}",
            new CreateAppResponse(app.Id.ToString(), dataPathResolver.ResolveFor(app.Slug), hints)
        );
    }

    private static IReadOnlyList<string> ResolveHelpfulNextSteps
    (
        string appTypeSlug,
        string slug,
        TypeStore typeStore,
        IReadOnlyList<(string SectionKey, JsonObject Overrides)> validatedOverrides,
        ProxySettings proxySettings
    )
    {
        // Only routed apps get the hosts hint -- a system-service or process-only registration
        // never gets a Caddy route in the first place. typeStore.HasBinding("routing") gates.
        if (!typeStore.HasBinding(appTypeSlug, "routing"))
        {
            return [];
        }

        var bindings = typeStore.GetBindings(appTypeSlug);

        if (bindings is null || !bindings.TryGetValue("routing", out var routingBindingJson))
        {
            return [];
        }

        string? overrideJson = null;

        foreach (var (sectionKey, overrideObject) in validatedOverrides)
        {
            if (string.Equals(sectionKey, "routing", StringComparison.Ordinal))
            {
                overrideJson = overrideObject.ToJsonString();
                break;
            }
        }

        var routing = CapabilityResolver.Resolve<RoutingConfiguration>(routingBindingJson, overrideJson);

        var hostname = CapabilityResolver.ResolveDomain(routing.DomainPattern, slug, proxySettings.BaseDomain);

        return [HostsHintBuilder.Compose(hostname)];
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076
