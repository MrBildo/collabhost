using System.Reflection;

using Collabhost.Api.Services;

namespace Collabhost.Api.Features.System;

public static class GetStatus
{
    public record Response
    (
        string Status,
        string Version,
        DateTimeOffset Timestamp,
        string ToolsDirectory
    );

    public static Ok<Response> Handle(PlatformSettings platformSettings, IWebHostEnvironment environment)
    {
        var fullVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0";

        var version = fullVersion.Split('+')[0];

        var toolsDirectory = Path.IsPathRooted(platformSettings.ToolsDirectory)
            ? platformSettings.ToolsDirectory
            : Path.GetFullPath(platformSettings.ToolsDirectory, environment.ContentRootPath);

        return TypedResults.Ok(new Response("healthy", version, DateTimeOffset.UtcNow, toolsDirectory));
    }
}
