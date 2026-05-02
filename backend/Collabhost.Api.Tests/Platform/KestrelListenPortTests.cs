using System.Diagnostics;
using System.Globalization;
using System.Reflection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Regression tests for card #176: the installed binary must bind Kestrel to Hosting:ListenPort
// (or its env-var override), not fall through to Kestrel's default :5000 while Caddy dials
// :58400. Separately, the dev path (ASPNETCORE_URLS set, which is what launchSettings.json
// and Aspire both produce) must continue to win -- the fix is a *fallback*, not an override.
public class KestrelListenPortTests
{
    [Fact]
    public async Task Startup_NoAspNetCoreUrls_BindsToHostingListenPort()
    {
        var port = GetFreePort();

        var listeningAddress = await StartAndReadListeningAddressAsync
        (
            env: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["COLLABHOST_HOSTING_LISTEN_PORT"] = port.ToString(CultureInfo.InvariantCulture)
            }
        );

        listeningAddress.ShouldNotBeNullOrWhiteSpace();

        listeningAddress!
            .ShouldBe
            (
                $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}",
                $"Kestrel should have bound to Hosting:ListenPort override, not default :5000. observed='{listeningAddress}'"
            );
    }

    [Fact]
    public async Task Startup_AspNetCoreUrlsSet_RespectsEnvOverride()
    {
        var port = GetFreePort();

        var listeningAddress = await StartAndReadListeningAddressAsync
        (
            env: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ASPNETCORE_URLS"] = $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}"
            }
        );

        listeningAddress.ShouldNotBeNullOrWhiteSpace();

        listeningAddress!
            .ShouldBe
            (
                $"http://localhost:{port.ToString(CultureInfo.InvariantCulture)}",
                "Dev/Aspire path must still win: ASPNETCORE_URLS should beat Hosting:ListenPort fallback."
            );
    }

    private static async Task<string?> StartAndReadListeningAddressAsync(Dictionary<string, string> env)
    {
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var alienCwd = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-kestrel-port-test",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(alienCwd);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { assemblyPath! },
                WorkingDirectory = alienCwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            psi.Environment["COLLABHOST_DATA_PATH"] = Path.Combine(alienCwd, "data");
            psi.Environment["COLLABHOST_USER_TYPES_PATH"] = Path.Combine(alienCwd, "usertypes");
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            psi.Environment["DOTNET_ENVIRONMENT"] = "Production";

            foreach (var kvp in env)
            {
                psi.Environment[kvp.Key] = kvp.Value;
            }

            using var process = Process.Start(psi);

            process.ShouldNotBeNull();

            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var listeningAddress = await ReadListeningAddressAsync(process!, readCts.Token);

            try
            {
                if (!process!.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited between HasExited and Kill -- benign.
            }

            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process!.WaitForExitAsync(exitCts.Token);

            return listeningAddress;
        }
        finally
        {
            try
            {
                Directory.Delete(alienCwd, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task<string?> ReadListeningAddressAsync(Process process, CancellationToken ct)
    {
        // Microsoft.Hosting.Lifetime writes "Now listening on: <address>" once Kestrel has bound.
        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);

            if (line is null)
            {
                return null;
            }

            const string Marker = "Now listening on:";
            var index = line.IndexOf(Marker, StringComparison.Ordinal);

            if (index >= 0)
            {
                return line[(index + Marker.Length)..].Trim();
            }
        }

        return null;
    }

    private static int GetFreePort()
    {
        // Bind-to-zero trick: kernel picks a free ephemeral port; we close immediately and
        // hand the number to the subprocess. Identical pattern to PortAllocator.
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
