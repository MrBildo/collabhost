using Collabhost.Api.Domain.Catalogs;

namespace Collabhost.Api.Features.Capabilities;

public static class GetFieldOptions
{
    public record OptionValue(string Value, string DisplayName);

    public record FieldOptionSet(string CapabilitySlug, string FieldName, IReadOnlyList<OptionValue> Options);

    public record Response(IReadOnlyList<FieldOptionSet> FieldOptions);

    public static async Task<Ok<Response>> HandleAsync
    (
        CommandDispatcher dispatcher,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(new GetFieldOptionsCommand(), cancellationToken);

        return TypedResults.Ok(result.Value!);
    }
}

public record GetFieldOptionsCommand : ICommand<GetFieldOptions.Response>;

public sealed class GetFieldOptionsCommandHandler
    : ICommandHandler<GetFieldOptionsCommand, GetFieldOptions.Response>
{
    public Task<CommandResult<GetFieldOptions.Response>> HandleAsync
    (
        GetFieldOptionsCommand command,
        CancellationToken ct = default
    )
    {
        var fieldOptions = new List<GetFieldOptions.FieldOptionSet>
        {
            new
            (
                StringCatalog.Capabilities.Restart,
                "policy",
                [
                    new GetFieldOptions.OptionValue(StringCatalog.RestartPolicies.Never, DisplayNames.RestartPolicies.Never),
                    new GetFieldOptions.OptionValue(StringCatalog.RestartPolicies.OnCrash, DisplayNames.RestartPolicies.OnCrash),
                    new GetFieldOptions.OptionValue(StringCatalog.RestartPolicies.Always, DisplayNames.RestartPolicies.Always)
                ]
            ),
            new
            (
                StringCatalog.Capabilities.Routing,
                "serveMode",
                [
                    new GetFieldOptions.OptionValue(StringCatalog.ServeModes.ReverseProxy, DisplayNames.ServeModes.ReverseProxy),
                    new GetFieldOptions.OptionValue(StringCatalog.ServeModes.FileServer, DisplayNames.ServeModes.FileServer)
                ]
            ),
            new
            (
                StringCatalog.Capabilities.Process,
                "discoveryStrategy",
                [
                    new GetFieldOptions.OptionValue(StringCatalog.DiscoveryStrategies.DotNetRuntimeConfig, DisplayNames.DiscoveryStrategies.DotNetRuntimeConfig),
                    new GetFieldOptions.OptionValue(StringCatalog.DiscoveryStrategies.PackageJson, DisplayNames.DiscoveryStrategies.PackageJson),
                    new GetFieldOptions.OptionValue(StringCatalog.DiscoveryStrategies.Manual, DisplayNames.DiscoveryStrategies.Manual)
                ]
            )
        };

        var response = new GetFieldOptions.Response(fieldOptions);

        return Task.FromResult(CommandResult<GetFieldOptions.Response>.Success(response));
    }
}
