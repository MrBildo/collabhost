using System.Reflection;

namespace Collabhost.Api.Features.System;

public static class GetStatus
{
    public record Response(string Status, string Version, DateTimeOffset Timestamp);

    public static Ok<Response> Handle()
    {
        var fullVersion = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.1.0";

        var version = fullVersion.Split('+')[0];

        return TypedResults.Ok(new Response("healthy", version, DateTimeOffset.UtcNow));
    }
}
