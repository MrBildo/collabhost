using System.Net.Http.Json;
using System.Text.Json;

using Collabhost.Api.Tests.Fixtures;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Integration tests for uptimeSeconds on GET /api/v1/status.
// Validates that the shared IApplicationStartTime service reports a sensible platform uptime --
// non-negative and monotonic. The source-of-truth is the OS process start time. Card #408,
// which subsumes the earlier 0 and -0 race from a JIT-initialized static start-time field.
[Collection("Api")]
public class SystemStatusUptimeTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture;

    [Fact]
    public async Task GetStatus_UptimeSeconds_IsNonNegative()
    {
        // Hit the endpoint immediately after factory creation -- the scenario that
        // previously could return 0 or -0 depending on JIT timing.
        using var response = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        response.IsSuccessStatusCode.ShouldBeTrue();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.TryGetProperty("uptimeSeconds", out var uptimeElement).ShouldBeTrue("uptimeSeconds field must be present");

        var uptimeSeconds = uptimeElement.GetDouble();

        uptimeSeconds.ShouldBeGreaterThanOrEqualTo(0.0, "uptimeSeconds must not be negative");
    }

    [Fact]
    public async Task GetStatus_TwoConsecutiveCalls_UptimeIsMonotonicallyIncreasing()
    {
        // Two back-to-back calls: the second must report >= uptime of the first.
        // A regressing value means the start-time source is non-deterministic (the old bug shape).
        using var response1 = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));
        using var response2 = await _fixture.Client.GetAsync(new Uri("/api/v1/status", UriKind.Relative));

        response1.IsSuccessStatusCode.ShouldBeTrue();
        response2.IsSuccessStatusCode.ShouldBeTrue();

        var json1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        var json2 = await response2.Content.ReadFromJsonAsync<JsonElement>();

        var uptime1 = json1.GetProperty("uptimeSeconds").GetDouble();
        var uptime2 = json2.GetProperty("uptimeSeconds").GetDouble();

        uptime2.ShouldBeGreaterThanOrEqualTo(uptime1, "uptimeSeconds must not decrease between consecutive calls");
    }
}
