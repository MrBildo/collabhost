using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Probes;
using Collabhost.Api.Proxy;
using Collabhost.Api.StaticSite;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
internal static class AppSettingsEndpoints
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    internal static async Task<IResult> GetAppSettingsAsync
    (
        string slug,
        AppStore store,
        TypeStore typeStore,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var bindings = typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await store.GetOverridesAsync(app.Id, ct);
        var appTypeDefinition = typeStore.GetBySlug(app.AppTypeSlug);

        var sections = BuildSettingsSections(app, bindings, overrides);

        var settings = new AppSettings
        (
            app.Id.ToString(),
            app.Slug,
            app.DisplayName,
            appTypeDefinition?.DisplayName ?? app.AppTypeSlug,
            app.RegisteredAt.ToString("o", CultureInfo.InvariantCulture),
            sections
        );

        return TypedResults.Ok(settings);
    }

    internal static async Task<IResult> SaveAppSettingsAsync
    (
        string slug,
        UpdateSettingsRequest request,
        AppStore store,
        TypeStore typeStore,
        ProbeService probeService,
        ProxyManager proxy,
        RuntimeConfigFileWriter runtimeConfigFileWriter,
        ExternalTargetSettings externalTargetSettings,
        ICurrentUser currentUser,
        ActivityEventStore activityEventStore,
        CancellationToken ct
    )
    {
        var app = await store.GetBySlugAsync(slug, ct);

        if (app is null)
        {
            return TypedResults.NotFound();
        }

        var bindings = typeStore.GetBindings(app.AppTypeSlug);
        var overrides = await store.GetOverridesAsync(app.Id, ct);

        // Handle identity section changes
        if (request.Changes.TryGetValue("identity", out var identityChanges))
        {
            if (identityChanges.TryGetValue("displayName", out var displayNameElement))
            {
                var newDisplayName = displayNameElement.GetString();

                if (!string.IsNullOrWhiteSpace(newDisplayName))
                {
                    app.DisplayName = newDisplayName;
                    app.ModifiedAt = DateTime.UtcNow;

                    await store.UpdateAppAsync(app, ct);
                }
            }
        }

        // Handle capability section changes
        foreach (var (sectionKey, sectionChanges) in request.Changes)
        {
            if (string.Equals(sectionKey, "identity", StringComparison.Ordinal))
            {
                continue;
            }

            if (bindings is null || !bindings.ContainsKey(sectionKey))
            {
                continue;
            }

            // Validate edits against schema
            var proposedOverrides = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                proposedOverrides[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            var validationErrors = CapabilityResolver.ValidateEdits
            (
                sectionKey, proposedOverrides, false, externalTargetSettings.AllowPublicHosts
            );

            if (validationErrors.Count > 0)
            {
                return TypedResults.Problem
                (
                    string.Join("; ", validationErrors),
                    statusCode: 400
                );
            }

            // Merge with existing override or create new one
            var existingOverrideJson = overrides.TryGetValue(sectionKey, out var existing)
                ? existing.ConfigurationJson
                : null;

            var effectiveOverride = existingOverrideJson is not null
                ? JsonNode.Parse(existingOverrideJson)?.AsObject() ?? []
                : (JsonObject)[];

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                effectiveOverride[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            // Cross-field validation on the post-merge effective override.
            // ValidateEdits above ran cross-field defense-in-depth on the
            // in-flight delta only; this is the load-bearing check that
            // catches the two-step operator path (e.g. save STS in headers
            // first, later toggle EnableHsts -- neither delta alone would
            // trip the in-flight check, but the merged state would).
            var mergedValidationErrors = CapabilityResolver.ValidateMergedOverrides
            (
                sectionKey, effectiveOverride
            );

            if (mergedValidationErrors.Count > 0)
            {
                return TypedResults.Problem
                (
                    string.Join("; ", mergedValidationErrors),
                    statusCode: 400
                );
            }

            await store.SaveOverrideAsync
            (
                app.Id,
                sectionKey,
                effectiveOverride.ToJsonString(_jsonOptions),
                ct
            );
        }

        // Refresh and return full settings
        store.Invalidate(slug);
        store.InvalidateOverrides(app.Id);

        // Re-probe when artifact config changes (location or project root)
        if (request.Changes.ContainsKey("artifact"))
        {
            probeService.InvalidateProbeCache(app.Id);

            await probeService.RunProbesAsync(app.Id, ct);
        }

        // Re-render the runtime-config file when its capability values change and
        // the route is currently enabled. The route-enable path handles the
        // start-time write; this handles edits while the route is already live.
        // Write failure surfaces as a 409 with the override already persisted --
        // operator-actionable (the save did happen; the file on disk did not).
        //
        // Gate uses IsRouteEnabled (default-true when _routeStates has no entry),
        // NOT IsRouteExplicitlyEnabled (default-false). A routing-only app whose
        // route is up by default-fallback -- the production-common case for an
        // app that was running before Collabhost restarted and has never been
        // operator-stop/start-cycled since boot -- must still re-render on a
        // settings change. The narrower IsRouteExplicitlyEnabled gate (added
        // #350-era) defeated this in the production-common case and shipped
        // #336's rsync-clobber-protection as structurally inert. Card #365.
        if (request.Changes.ContainsKey("runtime-config-file")
            && proxy.IsRouteEnabled(app.Slug))
        {
            try
            {
                await runtimeConfigFileWriter.RenderAsync(app, ct);
            }
            catch (RuntimeConfigFileWriteException exception)
            {
                return TypedResults.Problem
                (
                    "Settings saved, but failed to write runtime-config file: " + exception.Message,
                    statusCode: 409
                );
            }
        }

        var changedCapabilities = request.Changes.Keys
            .Where(k => !string.Equals(k, "identity", StringComparison.Ordinal))
                .ToList();

        await activityEventStore.RecordAsync
        (
            new ActivityEvent
            {
                EventType = ActivityEventTypes.AppSettingsUpdated,
                ActorId = currentUser.UserId.ToString(),
                ActorName = currentUser.User.Name,
                AppId = app.Id.ToString(),
                AppSlug = app.Slug,
                MetadataJson = JsonSerializer.Serialize(new { changedCapabilities })
            },
            ct
        );

        var freshApp = await store.GetBySlugAsync(slug, ct)
            ?? throw new InvalidOperationException($"App '{slug}' not found after save.");

        var freshBindings = typeStore.GetBindings(freshApp.AppTypeSlug);
        var freshOverrides = await store.GetOverridesAsync(app.Id, ct);
        var appTypeDefinition = typeStore.GetBySlug(freshApp.AppTypeSlug);

        var sections = BuildSettingsSections(freshApp, freshBindings, freshOverrides);

        var settings = new AppSettings
        (
            freshApp.Id.ToString(),
            freshApp.Slug,
            freshApp.DisplayName,
            appTypeDefinition?.DisplayName ?? freshApp.AppTypeSlug,
            freshApp.RegisteredAt.ToString("o", CultureInfo.InvariantCulture),
            sections
        );

        return TypedResults.Ok(settings);
    }

    private static List<SettingsSection> BuildSettingsSections
    (
        App app,
        IReadOnlyDictionary<string, string>? bindings,
        IReadOnlyDictionary<string, CapabilityOverride> overrides
    )
    {
        var sections = new List<SettingsSection>
        {
            // Identity section (always first)
            new            (
                "identity",
                "Identity",
                [
                    new SettingsField
                    (
                        "name",
                        "Name (slug)",
                        "text",
                        app.Slug,
                        app.Slug,
                        new FieldEditableLocked("Set during registration")
                    ),
                    new SettingsField
                    (
                        "displayName",
                        "Display Name",
                        "text",
                        app.DisplayName,
                        app.DisplayName,
                        new FieldEditableAlways()
                    )
                ]
            )
        };

        if (bindings is null)
        {
            return sections;
        }

        // Capability sections
        foreach (var (capabilitySlug, defaultConfigurationJson) in bindings.OrderBy(b => b.Key.GetCapabilityOrder()))
        {
            var definition = CapabilityCatalog.Get(capabilitySlug);

            if (definition is null)
            {
                continue;
            }

            var overrideJson = overrides.TryGetValue(capabilitySlug, out var capabilityOverride)
                ? capabilityOverride.ConfigurationJson
                : null;

            var effectiveJson = CapabilityResolver.ResolveJson
            (
                defaultConfigurationJson, overrideJson
            );

            JsonObject? effectiveValues = null;
            JsonObject? defaultValues = null;

            try
            {
                effectiveValues = JsonNode.Parse(effectiveJson)?.AsObject();
                defaultValues = JsonNode.Parse(defaultConfigurationJson)?.AsObject();
            }
            catch (JsonException)
            {
                continue;
            }

            if (effectiveValues is null || defaultValues is null)
            {
                continue;
            }

            var schemaOverrides = SchemaOverrides.Extract(defaultConfigurationJson);

            var fields = new List<SettingsField>();

            foreach (var baseDescriptor in definition.Schema)
            {
                schemaOverrides.TryGetValue(baseDescriptor.Key, out var fieldOverride);
                var fieldDescriptor = SchemaOverrides.Apply(baseDescriptor, fieldOverride);

                var value = effectiveValues.GetFieldValue(fieldDescriptor.Key);
                var defaultValue = defaultValues.GetFieldValue(fieldDescriptor.Key);

                fields.Add
                (
                    new SettingsField
                    (
                        fieldDescriptor.Key,
                        fieldDescriptor.Label,
                        fieldDescriptor.Type.ToString().ToLowerInvariant(),
                        value,
                        defaultValue,
                        fieldDescriptor.Editable,
                        fieldDescriptor.RequiresRestart,
                        fieldDescriptor.Options?
                            .Select(o => new FieldOption(o.Value.ToCamelCase(), o.Label))
                                .ToList(),
                        fieldDescriptor.HelpText,
                        fieldDescriptor.Unit,
                        fieldDescriptor.KeyPattern,
                        fieldDescriptor.KeyPatternMessage,
                        // #338. CamelCase the dependency value so it matches
                        // the camelCased Select option values above. Boolean
                        // parents ship "true"/"false" -- already lowercase
                        // (ToCamelCase is a no-op). Enum-name parents
                        // ("FileServer", "Manual") become "fileServer"/"manual"
                        // to align with the FE's view of the parent value.
                        fieldDescriptor.DependsOn is { } dependsOn
                            ? new FieldDependency(dependsOn.Field, dependsOn.Value.ToCamelCase())
                            : null,
                        // Card #348: per-field validation hints (Required /
                        // ValuePattern / MinValue / MaxValue) surface so the FE
                        // can mirror the server's authoritative rule.
                        fieldDescriptor.Required,
                        fieldDescriptor.ValuePattern,
                        fieldDescriptor.ValuePatternMessage,
                        fieldDescriptor.MinValue,
                        fieldDescriptor.MaxValue
                    )
                );
            }

            sections.Add
            (
                new SettingsSection
                (
                    capabilitySlug,
                    definition.DisplayName,
                    fields
                )
            );
        }

        return sections;
    }
}
#pragma warning restore MA0011
#pragma warning restore MA0076

file static class AppSettingsFieldExtensions
{
    extension(JsonObject obj)
    {
        public object? GetFieldValue(string key) =>
            !obj.TryGetPropertyValue(key, out var node) || node is null
                ? null
                : node.GetValueKind() switch
                {
                    JsonValueKind.String => node.GetValue<string>(),
                    JsonValueKind.Number => node.GetValue<double>(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, string>>(node),
                    _ => null
                };
    }

    extension(string value)
    {
        public string ToCamelCase() =>
            string.IsNullOrEmpty(value)
                ? value
                : char.ToLowerInvariant(value[0]) + value[1..];

        public int GetCapabilityOrder() => value switch
        {
            "process" => 0,
            "port-injection" => 1,
            "external-target" => 2, // Card #348: keep adjacent to routing -- the upstream-target capability sits with the routing concern.
            "routing" => 3,
            "health-check" => 4,
            "restart" => 5,
            "auto-start" => 6,
            "environment-defaults" => 7,
            "runtime-config-file" => 8,
            "artifact" => 9,
            _ => 99
        };
    }
}
