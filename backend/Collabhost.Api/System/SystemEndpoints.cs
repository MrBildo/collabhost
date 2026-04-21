using System.Globalization;

using Microsoft.AspNetCore.Http.HttpResults;

namespace Collabhost.Api.Platform;

public static class SystemEndpoints
{
    private static readonly DateTime _startedAt = DateTime.UtcNow;

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1").WithTags("System");

        group.MapGet("/status", GetStatus);
        group.MapGet("/version", GetVersion);
    }

    private static Ok<SystemStatus> GetStatus()
    {
        var uptimeSeconds = (DateTime.UtcNow - _startedAt).TotalSeconds;

        var status = new SystemStatus
        (
            "ok",
            VersionInfo.Current,
            Environment.MachineName,
            Math.Round(uptimeSeconds, 1),
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        );

        return TypedResults.Ok(status);
    }

    private static Ok<VersionResponse> GetVersion() =>
        TypedResults.Ok
        (
            new VersionResponse
            (
                VersionInfo.Current,
                VersionInfo.Commit,
                VersionInfo.Platform
            )
        );
}
