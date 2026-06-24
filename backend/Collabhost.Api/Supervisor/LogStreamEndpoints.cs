using System.Globalization;
using System.Threading.Channels;

using Collabhost.Api.Events;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Supervisor;

public static class LogStreamEndpoints
{
    private static int _concurrentStreams;
    private const int _maxConcurrentStreams = 10;

    // Keepalive cadence for the live SSE loop. 30s sits comfortably under the typical 60s
    // idle-read timeout of reverse proxies (Caddy included) and clients, so an idle stream
    // emits a heartbeat before anything upstream drops it. Card #437. Mutable private static
    // (not const) purely so tests can shorten the cadence via reflection -- the same seam the
    // concurrent-stream counter uses. Production always runs the 30 second default.
    //
    // IDE0044 (make readonly) is a genuine false positive here: production never reassigns the
    // field, but the test seam mutates it through reflection, which the analyzer cannot see.
    // readonly would defeat the test-only override; scoped-disable is the right escape hatch.
#pragma warning disable IDE0044 // Add readonly modifier
    private static TimeSpan _keepaliveInterval = TimeSpan.FromSeconds(30);
#pragma warning restore IDE0044 // Add readonly modifier

    public static void Map(IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/apps").WithTags("Apps");

        group.MapGet("/{slug}/logs/stream", StreamAppLogsAsync);
    }

    private static async Task StreamAppLogsAsync
    (
        string slug,
        AppStore store,
        ProcessSupervisor supervisor,
        IEventBus<ProcessStateChangedEvent> eventBus,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        if (Interlocked.Increment(ref _concurrentStreams) > _maxConcurrentStreams)
        {
            Interlocked.Decrement(ref _concurrentStreams);
            httpContext.Response.Headers.RetryAfter = "5";
            httpContext.Response.StatusCode = 503;
            return;
        }

        try
        {
            var app = await store.GetBySlugAsync(slug, ct);

            if (app is null)
            {
                Interlocked.Decrement(ref _concurrentStreams);
                httpContext.Response.StatusCode = 404;
                return;
            }

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";
            httpContext.Response.Headers.Connection = "keep-alive";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no";

            var buffer = supervisor.GetOrCreateLogBuffer(app.Id);

            // Subscribe BEFORE reading history to prevent gaps
            var logReader = buffer.Subscribe();

            var statusChannel = Channel.CreateBounded<ProcessStateChangedEvent>(32);

            using var statusSubscription = eventBus.Subscribe
            (
                e =>
                {
                    if (e.AppId == app.Id)
                    {
                        statusChannel.Writer.TryWrite(e);
                    }
                }
            );

            try
            {
                // Parse resume point from Last-Event-ID header (browser auto-reconnect)
                // or lastEventId query param (manual reconnect -- EventSource cannot set headers)
                long? lastEventId = null;

                var headerValue = httpContext.Request.Headers["Last-Event-ID"].FirstOrDefault();

                if (headerValue is not null
                    && long.TryParse(headerValue, CultureInfo.InvariantCulture, out var parsedHeader))
                {
                    lastEventId = parsedHeader;
                }

                if (lastEventId is null)
                {
                    var queryValue = httpContext.Request.Query["lastEventId"].FirstOrDefault();

                    if (queryValue is not null
                        && long.TryParse(queryValue, CultureInfo.InvariantCulture, out var parsedQuery))
                    {
                        lastEventId = parsedQuery;
                    }
                }

                // History burst -- send entries after the resume point, or full burst on fresh connect
                var history = buffer.GetLastWithIds(200);
                var lastSentId = lastEventId ?? 0L;

                foreach (var (id, entry) in history)
                {
                    if (id > lastSentId)
                    {
                        await SseWriter.WriteLogEventAsync(httpContext.Response, id, entry, ct);
                        lastSentId = id;
                    }
                }

                await httpContext.Response.Body.FlushAsync(ct);

                // Live loop — keepalive task created once, renewed only after completion
                // to avoid PeriodicTimer.WaitForNextTickAsync concurrent call crash
                using var keepaliveTimer = new PeriodicTimer(_keepaliveInterval);

                Task<bool> WaitForKeepaliveAsync() =>
                    keepaliveTimer.WaitForNextTickAsync(ct).AsTask();

                var keepaliveTask = WaitForKeepaliveAsync();

                while (!ct.IsCancellationRequested)
                {
                    var logTask = logReader.WaitToReadAsync(ct).AsTask();
                    var statusTask = statusChannel.Reader.WaitToReadAsync(ct).AsTask();

                    await Task.WhenAny(logTask, statusTask, keepaliveTask);

                    // Drain all available log entries
                    while (logReader.TryRead(out var logItem))
                    {
                        if (logItem.Id > lastSentId)
                        {
                            await SseWriter.WriteLogEventAsync
                            (
                                httpContext.Response, logItem.Id, logItem.Item, ct
                            );

                            lastSentId = logItem.Id;
                        }
                    }

                    // Drain all available status events
                    while (statusChannel.Reader.TryRead(out var statusEvent))
                    {
                        var stateString = statusEvent.NewState.ToApiString();

                        await SseWriter.WriteStatusEventAsync
                        (
                            httpContext.Response, stateString, ct
                        );
                    }

                    // Only renew keepalive task after it completes
                    if (keepaliveTask.IsCompletedSuccessfully)
                    {
                        await SseWriter.WriteKeepaliveAsync(httpContext.Response, ct);
                        keepaliveTask = WaitForKeepaliveAsync();
                    }
                }
            }
            finally
            {
                buffer.Unsubscribe(logReader);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected -- normal SSE lifecycle
        }
        finally
        {
            Interlocked.Decrement(ref _concurrentStreams);
        }
    }
}

file static class SseWriter
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteLogEventAsync
    (
        HttpResponse response,
        long id,
        LogEntry entry,
        CancellationToken ct
    )
    {
        var payload = new
        {
            id,
            timestamp = entry.Timestamp.ToString("o", CultureInfo.InvariantCulture),
            stream = entry.Stream == LogStream.StdOut ? "stdout" : "stderr",
            content = entry.Content,
            level = entry.Level
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        await response.WriteAsync($"id: {id.ToString(CultureInfo.InvariantCulture)}\nevent: log\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteStatusEventAsync
    (
        HttpResponse response,
        string state,
        CancellationToken ct
    )
    {
        var payload = new
        {
            state,
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        await response.WriteAsync($"event: status\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteClosedEventAsync
    (
        HttpResponse response,
        string reason,
        CancellationToken ct
    )
    {
        var payload = new { reason };
        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        await response.WriteAsync($"event: closed\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    public static async Task WriteKeepaliveAsync(HttpResponse response, CancellationToken ct)
    {
        // A REAL named event (not an SSE `:comment`): EventSource ignores comment lines, so the
        // FE listener can only subscribe to a named `keepalive` event. The timestamp payload
        // keeps the shape consistent with the status/closed events and gives the client an
        // idle-reset signal it can inspect. Card #437.
        var payload = new
        {
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        await response.WriteAsync($"event: keepalive\ndata: {json}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
