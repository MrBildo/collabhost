using System.Diagnostics;
using System.Reflection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Regression test for card #164 Finding 3: the installed binary must use its own location
// as the host's ContentRootPath, not the operator's shell CWD. Without this, the shipped
// appsettings.json is invisible, WebRootPath points at CWD, and relative data/ lands
// wherever the operator happened to be standing when they typed `collabhost`.
public class ContentRootTests
{
    [Fact]
    public async Task Startup_FromUnrelatedCwd_AnchorsContentRootToBinaryDirectory()
    {
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var binaryDirectory = Path.GetDirectoryName(assemblyPath!);

        binaryDirectory.ShouldNotBeNullOrWhiteSpace();

        // A unique, unrelated working directory for the subprocess. The binary must not
        // resolve its content root from this path even though the operator launched it
        // from here.
        var alienCwd = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-contentroot-test",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(alienCwd);

        try
        {
            // Dotnet-host binary is invoked with no arguments; the Program reports the
            // resolved ContentRootPath via Microsoft.Hosting.Lifetime before entering the
            // Kestrel event loop. Kill the process once that line lands.
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { assemblyPath! },
                WorkingDirectory = alienCwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            // Isolated data and user-types dirs so the subprocess cannot interact with any
            // developer environment. Env-var precedence wins over appsettings.
            var tempDataPath = Path.Combine(alienCwd, "data");
            var tempUserTypesPath = Path.Combine(alienCwd, "usertypes");
            psi.Environment["COLLABHOST_DATA_PATH"] = tempDataPath;
            psi.Environment["COLLABHOST_USER_TYPES_PATH"] = tempUserTypesPath;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            psi.Environment["DOTNET_ENVIRONMENT"] = "Production";

            using var process = Process.Start(psi);

            process.ShouldNotBeNull();

            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var observedContentRoot = await ReadContentRootPathAsync(process!, readCts.Token);

            try
            {
                if (!process!.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited between HasExited check and Kill -- benign.
            }

            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process!.WaitForExitAsync(exitCts.Token);

            observedContentRoot.ShouldNotBeNullOrWhiteSpace();

            // The reported Content root path should match the binary directory, NOT the
            // alien CWD. Path.GetFullPath normalizes trailing separators on both sides so
            // the comparison survives platform differences. Windows filesystems are
            // case-insensitive so the comparisons ignore case.
            var normalizedObserved = Path.GetFullPath(observedContentRoot!)
                .TrimEnd(Path.DirectorySeparatorChar);

            var normalizedBinaryDir = Path.GetFullPath(binaryDirectory!)
                .TrimEnd(Path.DirectorySeparatorChar);

            var normalizedAlienCwd = Path.GetFullPath(alienCwd)
                .TrimEnd(Path.DirectorySeparatorChar);

            string.Equals(normalizedObserved, normalizedBinaryDir, StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue
                (
                    $"Content root should be the binary directory. observed='{normalizedObserved}' expected='{normalizedBinaryDir}'"
                );

            string.Equals(normalizedObserved, normalizedAlienCwd, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse
                (
                    $"Content root must not be the operator's CWD. observed='{normalizedObserved}' alien='{normalizedAlienCwd}'"
                );
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

    private static async Task<string?> ReadContentRootPathAsync(Process process, CancellationToken ct)
    {
        // The Microsoft.Hosting.Lifetime logger writes "Content root path: <path>" during
        // startup. Read until that line appears or the token cancels.
        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);

            if (line is null)
            {
                return null;
            }

            const string Marker = "Content root path:";
            var index = line.IndexOf(Marker, StringComparison.Ordinal);

            if (index >= 0)
            {
                return line[(index + Marker.Length)..].Trim();
            }
        }

        return null;
    }
}
