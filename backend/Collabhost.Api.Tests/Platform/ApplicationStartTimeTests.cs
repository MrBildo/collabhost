using Collabhost.Api.Platform;

using Microsoft.Extensions.Hosting;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

public class ApplicationStartTimeTests
{
    [Fact]
    public void UtcStartedAt_BeforeApplicationStarted_ReturnsMinValue()
    {
        // ApplicationStarted not fired yet -- UtcStartedAt falls back to DateTime.MinValue.
        using var lifetime = new FakeHostApplicationLifetime();

        var sut = new ApplicationStartTime(lifetime);

        sut.UtcStartedAt.ShouldBe(DateTime.MinValue);
    }

    [Fact]
    public void UtcStartedAt_AfterApplicationStarted_ReturnsSnapshotNearFireTime()
    {
        using var lifetime = new FakeHostApplicationLifetime();

        var before = DateTime.UtcNow;

        var sut = new ApplicationStartTime(lifetime);

        lifetime.FireApplicationStarted();

        var after = DateTime.UtcNow;

        sut.UtcStartedAt.ShouldBeGreaterThanOrEqualTo(before);
        sut.UtcStartedAt.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void UtcStartedAt_KindIsUtc()
    {
        using var lifetime = new FakeHostApplicationLifetime();

        var sut = new ApplicationStartTime(lifetime);

        lifetime.FireApplicationStarted();

        sut.UtcStartedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }
}

// Fake IHostApplicationLifetime that exposes FireApplicationStarted() for test control.
// Sealed: file-scoped test double, no inheritance needed.
file sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _startedSource = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private readonly CancellationTokenSource _stoppedSource = new();

    public CancellationToken ApplicationStarted => _startedSource.Token;
    public CancellationToken ApplicationStopping => _stoppingSource.Token;
    public CancellationToken ApplicationStopped => _stoppedSource.Token;

    public void StopApplication() { }

    public void FireApplicationStarted() => _startedSource.Cancel();

    public void Dispose()
    {
        _startedSource.Dispose();
        _stoppingSource.Dispose();
        _stoppedSource.Dispose();
    }
}
