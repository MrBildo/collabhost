using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Operations;

namespace Collabhost.Api.Registry;

#pragma warning disable MA0076 // Ulid.ToString is not locale-sensitive
#pragma warning disable MA0011 // Ulid.ToString/TryParse is not locale-sensitive
internal static class AppSettingsEndpoints
{
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

    // Migrated to the operation spine (code-structure-conventions §8): the endpoint is a thin
    // adapter -- adapt UpdateSettingsRequest's typed nested dictionary into the normalized
    // UpdateSettingsCommand (REST flags: ValidateMergedOverrides + RefreshProbesOnArtifactChange
    // true -- REST's pre-migration behavior; RejectUnknownSection false -- REST skips unknown
    // sections), inject the concrete operation directly (no dispatcher), call it, and map the
    // OperationResult back to exactly the result the handler returned before. The shared validate ->
    // merge -> save -> render -> event loop now lives once in UpdateSettingsOperation.
    //
    // REST owns the SUCCESS result shaping: it re-fetches the app and rebuilds the full AppSettings
    // sections via BuildSettingsSections (REST result-mapping the endpoint owns -- the MCP surface
    // returns a fixed message instead, so the section rebuild is not in the operation). The save has
    // already happened in the operation, so the re-fetch reflects it.
    internal static async Task<IResult> SaveAppSettingsAsync
    (
        string slug,
        UpdateSettingsRequest request,
        UpdateSettingsOperation operation,
        AppStore store,
        TypeStore typeStore,
        CancellationToken ct
    )
    {
        var changes = request.ToJsonObject();

        var command = new UpdateSettingsCommand
        (
            slug,
            changes,
            ValidateMergedOverrides: true,
            RefreshProbesOnArtifactChange: true,
            RejectUnknownSection: false
        );

        var result = await operation.ExecuteAsync(command, ct);

        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        // Re-fetch + rebuild the full settings view (REST-only success shape). slug == the saved
        // app's slug; identity edits change only the display name, never the slug.
        var freshApp = await store.GetBySlugAsync(slug, ct)
            ?? throw new InvalidOperationException($"App '{slug}' not found after save.");

        var freshBindings = typeStore.GetBindings(freshApp.AppTypeSlug);
        var freshOverrides = await store.GetOverridesAsync(freshApp.Id, ct);
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

// File-scoped adapters between the REST surface and the operation spine (§7: the surface holds only
// its file-scoped mapping). The request adapter normalizes UpdateSettingsRequest's typed nested
// dictionary into the JsonObject the operation walks (each JsonElement -> JsonNode by raw text,
// byte-identical to the pre-migration per-field JsonNode.Parse(fieldValue.GetRawText())). The result
// mapping is the FAILURE half (success is shaped inline in SaveAppSettingsAsync, which re-fetches +
// rebuilds the full AppSettings).
file static class AppSettingsOperationAdapter
{
    public static JsonObject ToJsonObject(this UpdateSettingsRequest request)
    {
        var changes = new JsonObject();

        foreach (var (sectionKey, sectionChanges) in request.Changes)
        {
            var section = new JsonObject();

            foreach (var (fieldKey, fieldValue) in sectionChanges)
            {
                section[fieldKey] = JsonNode.Parse(fieldValue.GetRawText());
            }

            changes[sectionKey] = section;
        }

        return changes;
    }

    // K-1 (Kai's PR-1 forward note): OperationResult.FailureKind defaults to ordinal-0 NotFound on a
    // success, so the caller gates on IsSuccess BEFORE reaching this mapping -- it is the failure half
    // only. The three kinds map to the exact statuses the pre-migration handler returned: NotFound ->
    // 404 (empty body, as TypedResults.NotFound() did), Validation -> 400 (the bare section-qualified
    // joined errors, verbatim), Conflict -> 409 (the partial-success "Settings saved, but failed to
    // write..." message, with the override already persisted -- byte-identical to before).
    public static IResult ToHttpResult(this OperationResult<UpdateSettingsOutcome> result) =>
        result.FailureKind switch
        {
            OperationFailureKind.NotFound => TypedResults.NotFound(),
            OperationFailureKind.Validation => TypedResults.Problem(result.Error, statusCode: 400),
            _ => TypedResults.Problem(result.Error, statusCode: 409),
        };
}
