using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

using Collabhost.Api.Data;
using Collabhost.Api.Domain;
using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Apps;

public static class RunUpdate
{
    public const int DefaultUpdateTimeoutSeconds = 300;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

#pragma warning disable MA0051 // Long method justified — SSE streaming orchestration with sequential phases
    public static async Task HandleAsync
    (
        string externalId,
        CollabhostDbContext db,
        ProcessSupervisor supervisor,
        IManagedProcessRunner runner,
        UpdateCoordinator coordinator,
        HttpContext context,
        CancellationToken ct
    )
    {
        var app = await db.FindAppByExternalIdAsync(externalId, ct);

        if (app is null)
        {
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "NOT_FOUND", message = "App not found." }, ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(app.UpdateCommand))
        {
            context.Response.StatusCode = 400;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "NO_UPDATE_COMMAND", message = "No UpdateCommand configured for this app." }, ct);
            return;
        }

        if (!coordinator.TryAcquire(app.Id))
        {
            context.Response.StatusCode = 409;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "UPDATE_IN_PROGRESS", message = "An update is already running for this app." }, ct);
            return;
        }

        try
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            await context.Response.StartAsync(ct);

            var wasRunning = AppTypeBehavior.HasProcess(app.AppTypeId)
                && (supervisor.GetStatus(app.Id)?.IsRunning ?? false);

            if (wasRunning)
            {
                await WriteSseEventAsync(context, "status", new { phase = "stopping" }, ct);
                await supervisor.StopAppAsync(app.Id, ct);
            }

            await WriteSseEventAsync(context, "status", new { phase = "updating" }, ct);

            var fullApp = await db.Apps
                .Include(a => a.EnvironmentVariables)
                .SingleAsync(a => a.Id == app.Id, ct);

            var envVars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var ev in fullApp.EnvironmentVariables)
            {
                envVars[ev.Name] = ev.Value;
            }

            if (fullApp.Port.HasValue)
            {
                envVars["PORT"] = fullApp.Port.Value.ToString(CultureInfo.InvariantCulture);
            }

            var logChannel = Channel.CreateUnbounded<(string StreamName, string Line)>();

            var logWriterTask = Task.Run(async () =>
            {
                await foreach (var (streamName, line) in logChannel.Reader.ReadAllAsync(ct))
                {
                    await WriteSseEventAsync(context, "log", new { stream = streamName, line }, ct);
                }
            }, ct);

            var (shellCommand, shellArgs) = OperatingSystem.IsWindows()
                ? ("cmd.exe", $"/c {fullApp.UpdateCommand!}")
                : ("/bin/sh", $"-c \"{fullApp.UpdateCommand!}\"");

            var updateConfig = new ProcessStartConfig
            (
                shellCommand,
                shellArgs,
                fullApp.WorkingDirectory ?? fullApp.InstallDirectory,
                envVars,
                (line, stream) =>
                {
                    var streamName = stream == LogStream.StdOut ? "stdout" : "stderr";
                    logChannel.Writer.TryWrite((streamName, line));
                }
            );

            var timeout = TimeSpan.FromSeconds(fullApp.UpdateTimeoutSeconds ?? DefaultUpdateTimeoutSeconds);
            var runResult = await runner.RunToCompletionAsync(updateConfig, timeout, ct);

            logChannel.Writer.Complete();
            await logWriterTask;

            var success = runResult.ExitCode == 0 && !runResult.TimedOut;
            ProcessStatusResponse? processStatus = null;

            if (success && wasRunning)
            {
                await WriteSseEventAsync(context, "status", new { phase = "starting" }, ct);
                var managed = await supervisor.StartAppAsync(app.Id, ct);
                processStatus = ProcessStatusMapper.Map(managed);
            }

            var resultPhase = success ? "complete" : "failed";
            await WriteSseEventAsync(context, "status", new { phase = resultPhase }, ct);
            await WriteSseEventAsync
            (
                context,
                "result",
                new
                {
                    success,
                    exitCode = runResult.ExitCode,
                    timedOut = runResult.TimedOut,
                    processStatus
                },
                ct
            );
        }
        finally
        {
            coordinator.Release(app.Id);
        }
    }
#pragma warning restore MA0051

    private static async Task WriteSseEventAsync(HttpContext context, string eventType, object data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, _jsonOptions);
        await context.Response.WriteAsync($"event: {eventType}\ndata: {json}\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }
}
