using System.Net;

using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.HealthChecks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Xunit;

// Namespace deliberately differs from the source-side
// Collabhost.Api.HealthChecks: at the lookup site for HealthCheckStatus / HealthCheckResult,
// "Collabhost.Api.Tests.HealthChecks" is the enclosing namespace and is searched ahead
// of any using-imported namespace. With nothing matching inside it, lookup walked outward
// without finding anything and never reached the using directive. Renaming the test
// namespace to a non-conflicting suffix lets the using directive resolve normally.
namespace Collabhost.Api.Tests.HealthCheckExecutor;

public class HealthCheckProbeTests
{
    [Fact]
    public async Task ProbeAsync_2xxResponse_ReturnsHealthy()
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((request, _) =>
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/health");
            request.RequestUri.Host.ShouldBe("localhost");
            request.RequestUri.Port.ShouldBe(5000);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Healthy);
        result.LastError.ShouldBeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, HealthCheckStatus.Healthy)]
    [InlineData(HttpStatusCode.PartialContent, HealthCheckStatus.Healthy)]
    [InlineData(HttpStatusCode.NotFound, HealthCheckStatus.Unhealthy)]
    [InlineData(HttpStatusCode.InternalServerError, HealthCheckStatus.Unhealthy)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HealthCheckStatus.Unhealthy)]
    public async Task ProbeAsync_StatusCode_MapsToExpectedStatus(HttpStatusCode statusCode, HealthCheckStatus expected)
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
            new HttpResponseMessage(statusCode)));

        var result = await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.Status.ShouldBe(expected);
    }

    [Fact]
    public async Task ProbeAsync_NonSuccessStatus_RecordsHttpStatusInError()
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var result = await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.LastError.ShouldBe("HTTP 503");
    }

    [Fact]
    public async Task ProbeAsync_HttpRequestException_ReturnsUnhealthy()
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
            throw new HttpRequestException("connection refused")));

        var result = await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Unhealthy);
        result.LastError.ShouldNotBeNull();
        result.LastError.ShouldContain("connection refused");
    }

    [Fact]
    public async Task ProbeAsync_Timeout_ReturnsUnhealthyWithTimeoutLabel()
    {
        // Async-shape constructor is the right call here -- the handler awaits genuinely.
        var probe = BuildProbe(new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 1 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Unhealthy);
        result.LastError.ShouldBe("timeout");
    }

    [Fact]
    public async Task ProbeAsync_EmptyEndpoint_DefaultsToHealth()
    {
        var capturedPath = "";
        var probe = BuildProbe(StubHttpMessageHandler.Sync((request, _) =>
        {
            capturedPath = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        capturedPath.ShouldBe("/health");
    }

    [Fact]
    public async Task ProbeAsync_EndpointWithoutLeadingSlash_PrependsSlash()
    {
        var capturedPath = "";
        var probe = BuildProbe(StubHttpMessageHandler.Sync((request, _) =>
        {
            capturedPath = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await probe.ProbeAsync
        (
            "test-app",
            "localhost",
            5000,
            "http",
            new HealthCheckConfiguration { Endpoint = "status/ready", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        capturedPath.ShouldBe("/status/ready");
    }

    private static HealthCheckProbe BuildProbe(HttpMessageHandler handler) =>
        new
        (
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger<HealthCheckProbe>.Instance
        );
}

// Sealed: file-scoped HTTP test fake; no inheritance needed.
// Single async-shape constructor avoids overload ambiguity when callers pass a lambda
// without an explicit return type. Sync callers wrap with Task.FromResult at the call site.
file sealed class StubHttpMessageHandler
(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler
)
    : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

    public static StubHttpMessageHandler Sync
    (
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler
    ) =>
        new((request, ct) => Task.FromResult(handler(request, ct)));

    protected override Task<HttpResponseMessage> SendAsync
    (
        HttpRequestMessage request,
        CancellationToken cancellationToken
    ) =>
        _handler(request, cancellationToken);
}
