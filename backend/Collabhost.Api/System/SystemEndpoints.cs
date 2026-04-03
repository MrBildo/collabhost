using System.Diagnostics;
using System.Globalization;
using System.Reflection;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Collabhost.Api.Platform;

public static class SystemEndpoints
{
    private static readonly DateTime _startedAt = DateTime.UtcNow;

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("System");

        group.MapGet("/status", GetStatus);
    }

    private static Ok<SystemStatus> GetStatus()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        var uptimeSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds;

        var status = new SystemStatus
        (
            Status: "ok",
            Version: version,
            Hostname: Environment.MachineName,
            UptimeSeconds: Math.Round(uptimeSeconds, 1),
            Timestamp: DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        );

        return TypedResults.Ok(status);
    }
}
