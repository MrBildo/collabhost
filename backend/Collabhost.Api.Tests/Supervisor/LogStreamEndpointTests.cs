using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Events;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Supervisor;

[Collection("Api")]
public class LogStreamEndpointTests(ApiFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task StreamLogs_AppNotFound_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/nonexistent-app/logs/stream");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StreamLogs_MissingAuth_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/any-slug/logs/stream");

        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StreamLogs_QueryParamAuth_Succeeds()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream?key={ApiFixture.AdminKey}"
            );

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_HeaderAuth_Succeeds()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_HistoryBurst_SendsExistingEntries()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            // Write some log entries to the buffer before connecting
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "line one", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "line two", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, "error line", "ERR"));

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var events = await SseTestHelper.ReadEventsAsync(stream, 3, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(3);

            events[0].EventType.ShouldBe("log");
            events[0].Id.ShouldNotBeNull();

            var firstPayload = JsonDocument.Parse(events[0].Data);
            firstPayload.RootElement.GetProperty("content").GetString().ShouldBe("line one");
            firstPayload.RootElement.GetProperty("stream").GetString().ShouldBe("stdout");

            var thirdPayload = JsonDocument.Parse(events[2].Data);
            thirdPayload.RootElement.GetProperty("content").GetString().ShouldBe("error line");
            thirdPayload.RootElement.GetProperty("stream").GetString().ShouldBe("stderr");

            // Verify IDs are monotonically increasing
            var id1 = long.Parse(events[0].Id!, System.Globalization.CultureInfo.InvariantCulture);
            var id2 = long.Parse(events[1].Id!, System.Globalization.CultureInfo.InvariantCulture);
            var id3 = long.Parse(events[2].Id!, System.Globalization.CultureInfo.InvariantCulture);

            id2.ShouldBeGreaterThan(id1);
            id3.ShouldBeGreaterThan(id2);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_LiveEvents_DeliveredInOrder()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);

            // Write live entries after the stream is open
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "live one", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "live two", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "live three", "INF"));

            var events = await SseTestHelper.ReadEventsAsync(stream, 3, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(3);

            var payload1 = JsonDocument.Parse(events[0].Data);
            var payload2 = JsonDocument.Parse(events[1].Data);
            var payload3 = JsonDocument.Parse(events[2].Data);

            payload1.RootElement.GetProperty("content").GetString().ShouldBe("live one");
            payload2.RootElement.GetProperty("content").GetString().ShouldBe("live two");
            payload3.RootElement.GetProperty("content").GetString().ShouldBe("live three");

            // Verify IDs are monotonically increasing
            var id1 = payload1.RootElement.GetProperty("id").GetInt64();
            var id2 = payload2.RootElement.GetProperty("id").GetInt64();
            var id3 = payload3.RootElement.GetProperty("id").GetInt64();

            id2.ShouldBeGreaterThan(id1);
            id3.ShouldBeGreaterThan(id2);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_StatusEvents_DeliveredOnStateChange()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var eventBus = fixture.Services.GetRequiredService<IEventBus<ProcessStateChangedEvent>>();

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);

            // Give the SSE handler time to subscribe to the event bus
            await Task.Delay(200, cts.Token);

            // Publish a state change event
            eventBus.Publish
            (
                new ProcessStateChangedEvent
                (
                    appId,
                    slug,
                    ProcessState.Running,
                    ProcessState.Stopped,
                    null
                )
            );

            var events = await SseTestHelper.ReadEventsAsync(stream, 1, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(1);
            events[0].EventType.ShouldBe("status");

            var payload = JsonDocument.Parse(events[0].Data);
            payload.RootElement.GetProperty("state").GetString().ShouldBe("stopped");
            payload.RootElement.TryGetProperty("timestamp", out _).ShouldBeTrue();
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_ResponseHeaders_Correct()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
            response.Headers.CacheControl?.NoCache.ShouldBeTrue();

            response.Headers.TryGetValues("X-Accel-Buffering", out var accelValues).ShouldBeTrue();
            accelValues.ShouldContain("no");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_ConcurrentLimit_Returns503()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            // Use reflection to pre-set the concurrent stream counter to the max.
            // Maintaining 10 live SSE connections in TestServer is unreliable because
            // handlers may complete when the response pipe buffer fills or disconnects.
            var counterField = typeof(LogStreamEndpoints).GetField
            (
                "_concurrentStreams",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
            );

            counterField.ShouldNotBeNull("Static counter field should exist");

            var originalValue = (int)counterField.GetValue(null)!;

            try
            {
                counterField.SetValue(null, 10);

                using var request = new HttpRequestMessage
                (
                    HttpMethod.Get,
                    $"/api/v1/apps/{slug}/logs/stream"
                );

                request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

                using var response = await _client.SendAsync
                (
                    request, HttpCompletionOption.ResponseHeadersRead
                );

                response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

                response.Headers.TryGetValues("Retry-After", out var retryValues).ShouldBeTrue();
                retryValues.ShouldContain("5");
            }
            finally
            {
                // Restore original counter value so other tests aren't affected
                counterField.SetValue(null, originalValue);
            }
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_LastEventIdHeader_SkipsOlderEntries()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry one"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry two"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry three"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry four"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry five"));

            var withIds = buffer.GetLastWithIds(5);
            var resumeAfterId = withIds[2].Id;

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
            request.Headers.Add("Last-Event-ID", resumeAfterId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var events = await SseTestHelper.ReadEventsAsync(stream, 2, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(2);

            var payload1 = JsonDocument.Parse(events[0].Data);
            var payload2 = JsonDocument.Parse(events[1].Data);

            payload1.RootElement.GetProperty("content").GetString().ShouldBe("entry four");
            payload2.RootElement.GetProperty("content").GetString().ShouldBe("entry five");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_LastEventIdQueryParam_SkipsOlderEntries()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry one"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry two"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry three"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry four"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "entry five"));

            var withIds = buffer.GetLastWithIds(5);
            var resumeAfterId = withIds[2].Id;

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream?key={ApiFixture.AdminKey}&lastEventId={resumeAfterId.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            );

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var events = await SseTestHelper.ReadEventsAsync(stream, 2, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(2);

            var payload1 = JsonDocument.Parse(events[0].Data);
            var payload2 = JsonDocument.Parse(events[1].Data);

            payload1.RootElement.GetProperty("content").GetString().ShouldBe("entry four");
            payload2.RootElement.GetProperty("content").GetString().ShouldBe("entry five");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task StreamLogs_NoLastEventId_SendsFullBurst()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "first"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "second"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "third"));

            using var request = new HttpRequestMessage
            (
                HttpMethod.Get,
                $"/api/v1/apps/{slug}/logs/stream"
            );

            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await _client.SendAsync
            (
                request, HttpCompletionOption.ResponseHeadersRead, cts.Token
            );

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var events = await SseTestHelper.ReadEventsAsync(stream, 3, TimeSpan.FromSeconds(5));

            events.Count.ShouldBe(3);

            var payload1 = JsonDocument.Parse(events[0].Data);
            var payload2 = JsonDocument.Parse(events[1].Data);
            var payload3 = JsonDocument.Parse(events[2].Data);

            payload1.RootElement.GetProperty("content").GetString().ShouldBe("first");
            payload2.RootElement.GetProperty("content").GetString().ShouldBe("second");
            payload3.RootElement.GetProperty("content").GetString().ShouldBe("third");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetLogs_ReturnsEntriesWithIds()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "line one", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, "line two", "ERR"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "line three", "INF"));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/logs");
            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var entries = doc.RootElement.GetProperty("entries");

            entries.GetArrayLength().ShouldBe(3);

            var id1 = entries[0].GetProperty("id").GetInt64();
            var id2 = entries[1].GetProperty("id").GetInt64();
            var id3 = entries[2].GetProperty("id").GetInt64();

            id1.ShouldBeGreaterThan(0);
            id2.ShouldBeGreaterThan(id1);
            id3.ShouldBeGreaterThan(id2);

            entries[0].GetProperty("content").GetString().ShouldBe("line one");
            entries[0].GetProperty("stream").GetString().ShouldBe("stdout");
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetLogs_FilterByStream_ReturnsFilteredWithIds()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "stdout line", "INF"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, "stderr line", "ERR"));
            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "another stdout", "INF"));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/logs?stream=stdout");
            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);
            var entries = doc.RootElement.GetProperty("entries");

            entries.GetArrayLength().ShouldBe(2);

            entries[0].GetProperty("stream").GetString().ShouldBe("stdout");
            entries[1].GetProperty("stream").GetString().ShouldBe("stdout");

            var id1 = entries[0].GetProperty("id").GetInt64();
            var id2 = entries[1].GetProperty("id").GetInt64();

            id1.ShouldBeGreaterThan(0);
            id2.ShouldBeGreaterThan(id1);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetLogs_EmptyBuffer_ReturnsEmptyList()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}/logs");
            request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

            var response = await _client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(body);

            doc.RootElement.GetProperty("entries").GetArrayLength().ShouldBe(0);
            doc.RootElement.GetProperty("totalBuffered").GetInt32().ShouldBe(0);
        }
        finally
        {
            await DeleteTestAppAsync(slug);
        }
    }

    [Fact]
    public async Task GetLogs_AppNotFound_Returns404()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/apps/nonexistent-slug/logs");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task QueryParamAuth_NonSseEndpoint_Returns401()
    {
        using var request = new HttpRequestMessage
        (
            HttpMethod.Get,
            $"/api/v1/apps?key={ApiFixture.AdminKey}"
        );

        using var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteApp_CleansUpLogBuffer()
    {
        var slug = await RegisterTestAppAsync();

        try
        {
            var appId = await GetAppIdAsync(slug);
            var supervisor = fixture.Services.GetRequiredService<ProcessSupervisor>();
            var buffer = supervisor.GetOrCreateLogBuffer(appId);

            buffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdOut, "should be cleaned up"));

            buffer.Count.ShouldBe(1);

            await DeleteTestAppAsync(slug);

            // After deletion, GetOrCreateLogBuffer returns a fresh empty buffer
            var freshBuffer = supervisor.GetOrCreateLogBuffer(appId);

            freshBuffer.Count.ShouldBe(0);
        }
        catch
        {
            // Cleanup in case test fails before delete
            await DeleteTestAppAsync(slug);
            throw;
        }
    }

    private async Task<string> RegisterTestAppAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"test-sse-{suffix}";

        var createPayload = new
        {
            name = slug,
            displayName = "SSE Test App",
            appTypeSlug = "static-site"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/apps");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);
        request.Content = JsonContent.Create(createPayload, options: _jsonOptions);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return slug;
    }

    private async Task<Ulid> GetAppIdAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var detail = JsonDocument.Parse(body);
        var idString = detail.RootElement.GetProperty("id").GetString()!;

        return Ulid.Parse(idString, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task DeleteTestAppAsync(string slug)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/apps/{slug}");
        request.Headers.Add("X-User-Key", ApiFixture.AdminKey);

        await _client.SendAsync(request);
    }
}

file static class SseTestHelper
{
    public static async Task<List<SseEvent>> ReadEventsAsync
    (
        Stream stream,
        int count,
        TimeSpan timeout
    )
    {
        var events = new List<SseEvent>();
        using var reader = new StreamReader(stream);
        using var cts = new CancellationTokenSource(timeout);

        string? eventType = null;
        string? data = null;
        string? id = null;

        while (events.Count < count && !cts.Token.IsCancellationRequested)
        {
            string? line;

            try
            {
                line = await reader.ReadLineAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = line[6..];
            }
            else if (line.StartsWith("id: ", StringComparison.Ordinal))
            {
                id = line[4..];
            }
            else if (line.Length == 0 && data is not null)
            {
                events.Add(new SseEvent(eventType, data, id));
                eventType = null;
                data = null;
                id = null;
            }
        }

        return events;
    }
}

// No subclasses expected -- file-scoped test helper
file sealed record SseEvent(string? EventType, string Data, string? Id);
