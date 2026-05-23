using System.Net;

using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.HealthChecks;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using Xunit;

// Namespace deliberately differs from Collabhost.Api.HealthChecks (see the
// HealthCheckProbeTests file for the longer rationale -- nested-vs-outer
// namespace lookup precedence).
namespace Collabhost.Api.Tests.HealthCheckExecutor;

// Card #348 probe-side tests for external-target apps. These exercise the
// post-#348 signature (host, scheme threaded through) and the URL-shape it
// emits. The TickAsync gate-condition matrix is covered separately at
// integration level (ExternalRouteIntegrationTests) because it requires the
// full DI graph (TypeStore, AppStore, ProxyManager) which is not easily
// faked in isolation.
public class ExternalHealthCheckProbeTests
{
    [Fact]
    public async Task ProbeAsync_ExternalHost_BuildsUrlWithHostAndPort()
    {
        Uri? capturedUri = null;

        var probe = BuildProbe(StubHttpMessageHandler.Sync((request, _) =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await probe.ProbeAsync
        (
            "crawl4ai",
            "192.168.1.50",
            11235,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Healthy);
        capturedUri.ShouldNotBeNull();
        capturedUri!.Scheme.ShouldBe("http");
        capturedUri.Host.ShouldBe("192.168.1.50");
        capturedUri.Port.ShouldBe(11235);
        capturedUri.AbsolutePath.ShouldBe("/health");
    }

    [Fact]
    public async Task ProbeAsync_HttpsScheme_BuildsHttpsUrl()
    {
        Uri? capturedUri = null;

        var probe = BuildProbe(StubHttpMessageHandler.Sync((request, _) =>
        {
            capturedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        await probe.ProbeAsync
        (
            "upstream",
            "upstream.local",
            8443,
            "https",
            new HealthCheckConfiguration { Endpoint = "/healthz", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        capturedUri.ShouldNotBeNull();
        capturedUri!.Scheme.ShouldBe("https");
        capturedUri.Host.ShouldBe("upstream.local");
        capturedUri.Port.ShouldBe(8443);
    }

    [Fact]
    public async Task ProbeAsync_ServerError_ReturnsUnhealthyWithStatusDetail()
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var result = await probe.ProbeAsync
        (
            "crawl4ai",
            "10.0.0.5",
            8080,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Unhealthy);
        result.LastError.ShouldBe("HTTP 502");
    }

    [Fact]
    public async Task ProbeAsync_ConnectionRefused_ReturnsUnhealthyWithMessage()
    {
        var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
            throw new HttpRequestException("connection refused")));

        var result = await probe.ProbeAsync
        (
            "crawl4ai",
            "172.16.0.1",
            8080,
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
        var probe = BuildProbe(new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        var result = await probe.ProbeAsync
        (
            "crawl4ai",
            "upstream.local",
            11235,
            "http",
            new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 1 },
            CancellationToken.None
        );

        result.Status.ShouldBe(HealthCheckStatus.Unhealthy);
        result.LastError.ShouldBe("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProbeAsync_BlankHost_Throws(string? host) =>
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)));

            await probe.ProbeAsync
            (
                "x",
                host!,
                8080,
                "http",
                new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
                CancellationToken.None
            );
        });

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ProbeAsync_BlankScheme_Throws(string? scheme) =>
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            var probe = BuildProbe(StubHttpMessageHandler.Sync((_, _) =>
                new HttpResponseMessage(HttpStatusCode.OK)));

            await probe.ProbeAsync
            (
                "x",
                "localhost",
                8080,
                scheme!,
                new HealthCheckConfiguration { Endpoint = "/health", IntervalSeconds = 30, TimeoutSeconds = 5 },
                CancellationToken.None
            );
        });

    private static HealthCheckProbe BuildProbe(HttpMessageHandler handler) =>
        new
        (
            new HttpClient(handler),
            new FakeTimeProvider(),
            NullLogger<HealthCheckProbe>.Instance
        );
}

// File-scoped stub mirroring the shape in HealthCheckProbeTests so this file
// is self-contained. See that file for the design rationale.
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
