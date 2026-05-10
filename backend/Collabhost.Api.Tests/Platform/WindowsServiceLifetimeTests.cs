using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Card #237 Q4 -- service-aware Windows lifetime. The contract is:
//
//   1. Console-launch (Linux, macOS, Aspire, dotnet run, Windows-from-shell, integration tests):
//      UseWindowsService() is a no-op. The default console lifetime stays in effect; nothing
//      about hosted services, logging providers, or shutdown semantics changes.
//
//   2. SCM-launch (only the Windows Service Control Manager): WindowsServiceLifetime replaces
//      the console lifetime. Stop signals come through SERVICE_CONTROL_STOP, status reporting
//      flows back to the SCM, and the Windows Event Log logging provider is registered.
//
// The discriminator is WindowsServiceHelpers.IsWindowsService(). Test contexts (xunit,
// Aspire, dotnet run) never run under the SCM, so the helper always returns false here --
// which is exactly what the cross-platform-no-op contract relies on. The SCM-launch path is
// shape-tested via the helper itself; we don't need a real SCM to verify that the package's
// platform gate works.
public class WindowsServiceLifetimeTests
{
    [Fact]
    // The xunit runner is not the SCM. The helper inspects the parent process; if it sees the
    // SCM as the parent, it returns true. Anywhere else (this test, Aspire, dotnet run, Linux,
    // macOS) it returns false.
    public void IsWindowsService_InTestContext_ReturnsFalse() =>
        WindowsServiceHelpers.IsWindowsService().ShouldBeFalse();

    [Fact]
    public void UseWindowsService_OnGenericHostBuilder_ReturnsTheSameBuilder()
    {
        // The shape contract: UseWindowsService is an extension on IHostBuilder that returns
        // the same builder for chaining. The package's API surface is consistent across
        // platforms -- the call compiles and runs everywhere; the runtime decision (whether
        // to actually wire WindowsServiceLifetime) happens later, gated on
        // WindowsServiceHelpers.IsWindowsService(). Verifying the call returns the builder
        // proves the package's cross-platform fluent surface is wired correctly.
        var hostBuilder = Host.CreateDefaultBuilder();

        var returned = hostBuilder.UseWindowsService();

        returned.ShouldBeSameAs(hostBuilder);
    }

    [Fact]
    public async Task GenericHost_WithUseWindowsService_StartsAndStopsCleanly_OnAnyPlatform()
    {
        // End-to-end: a minimal generic host with UseWindowsService() wired must start and
        // stop without throwing on Linux, macOS, or Windows-from-shell. WindowsServiceLifetime
        // would attempt to talk to the SCM and would fail catastrophically here -- so the
        // success of this test on every CI leg proves the package's IsWindowsService gate is
        // doing its job (the lifetime is NOT registered when the helper returns false).
        using var host = Host.CreateDefaultBuilder()
            .UseWindowsService()
            .Build();

        await host.StartAsync(CancellationToken.None);

        // Lifetime survives a start cycle. The IHostApplicationLifetime token is in the
        // started state; if WindowsServiceLifetime had taken over inappropriately we'd have
        // thrown out of StartAsync above before reaching this assertion.
        var lifetime = host.Services.GetService(typeof(IHostApplicationLifetime)) as IHostApplicationLifetime;
        lifetime.ShouldNotBeNull();
        lifetime.ApplicationStarted.IsCancellationRequested.ShouldBeTrue();

        await host.StopAsync(CancellationToken.None);
    }
}
