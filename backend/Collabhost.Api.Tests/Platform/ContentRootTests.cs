using System.Diagnostics;
using System.Reflection;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Platform;

// Regression test for card #164 Finding 3: the installed binary must use its own location
// as the host's ContentRootPath, not the operator's shell CWD. Without this, the shipped
// appsettings.json is invisible, WebRootPath points at CWD, and relative data/ lands
// wherever the operator happened to be standing when they typed `collabhost`.
//
// Card #246 (c2-A) extends the contract: ASPNETCORE_CONTENTROOT can override the default,
// so the system-scope systemd unit can pin ContentRoot to the install root (where the
// canonical wwwroot/ lives) without relying on a symlink back to the binary directory.
// COLLABHOST_CONFIG_PATH lets the operator load appsettings.json from /etc/collabhost
// without a parallel symlink. The two new tests below cover the env-var-set path; the
// fallback path (env vars unset) is covered by Startup_FromUnrelatedCwd_*.
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

    [Fact]
    public async Task Startup_AspnetcoreContentrootSet_ResolvesToEnvVarPath()
    {
        // Card #246 (c2-A): when the system-scope systemd unit sets ASPNETCORE_CONTENTROOT,
        // the host must honor it. Without this, the explicit WebApplicationOptions.ContentRootPath
        // set in Program.cs would override the env var and pin ContentRoot to BaseDirectory --
        // defeating the whole point of the env var.
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var binaryDirectory = Path.GetDirectoryName(assemblyPath!);

        binaryDirectory.ShouldNotBeNullOrWhiteSpace();

        // Three distinct directories: alien CWD, env-var-pinned ContentRoot, and the binary
        // directory. The observed ContentRoot must match the env-var-pinned one, NOT
        // BaseDirectory and NOT the alien CWD.
        var alienCwd = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-contentroot-envvar-cwd",
            Guid.NewGuid().ToString("N")
        );

        var envContentRoot = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-contentroot-envvar-target",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(alienCwd);
        Directory.CreateDirectory(envContentRoot);

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

            var tempDataPath = Path.Combine(envContentRoot, "data");
            var tempUserTypesPath = Path.Combine(envContentRoot, "usertypes");
            psi.Environment["COLLABHOST_DATA_PATH"] = tempDataPath;
            psi.Environment["COLLABHOST_USER_TYPES_PATH"] = tempUserTypesPath;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
            psi.Environment["ASPNETCORE_CONTENTROOT"] = envContentRoot;

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

            var normalizedObserved = Path.GetFullPath(observedContentRoot!)
                .TrimEnd(Path.DirectorySeparatorChar);

            var normalizedEnvContentRoot = Path.GetFullPath(envContentRoot)
                .TrimEnd(Path.DirectorySeparatorChar);

            var normalizedBinaryDir = Path.GetFullPath(binaryDirectory!)
                .TrimEnd(Path.DirectorySeparatorChar);

            string.Equals(normalizedObserved, normalizedEnvContentRoot, StringComparison.OrdinalIgnoreCase)
                .ShouldBeTrue
                (
                    $"Content root should be the env-var-pinned path. observed='{normalizedObserved}' expected='{normalizedEnvContentRoot}'"
                );

            string.Equals(normalizedObserved, normalizedBinaryDir, StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse
                (
                    $"Content root must not fall through to BaseDirectory when ASPNETCORE_CONTENTROOT is set. observed='{normalizedObserved}' baseDir='{normalizedBinaryDir}'"
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

            try
            {
                Directory.Delete(envContentRoot, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task Startup_CollabhostConfigPathSet_LoadsConfigFromEnvVarPath()
    {
        // Card #246 (c2-A): when the system-scope systemd unit sets COLLABHOST_CONFIG_PATH,
        // the host must load that JSON file in addition to the framework's default
        // ContentRoot/appsettings.json. Verified end-to-end by writing a synthetic
        // appsettings.json with a custom Hosting:ListenPort and observing Kestrel bind to
        // that port (Microsoft.Hosting.Lifetime logs "Now listening on: http://localhost:<port>").
        var assemblyPath = Assembly.GetAssembly(typeof(Program))?.Location;

        assemblyPath.ShouldNotBeNullOrWhiteSpace();

        var binaryDirectory = Path.GetDirectoryName(assemblyPath!);

        binaryDirectory.ShouldNotBeNullOrWhiteSpace();

        var alienCwd = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-configpath-cwd",
            Guid.NewGuid().ToString("N")
        );

        var configDir = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-configpath-config",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(alienCwd);
        Directory.CreateDirectory(configDir);

        // Pick a high, unlikely-to-collide port for the sentinel. The port itself is the
        // load-bearing observation -- if the env-var-pointed config file is loaded, Kestrel
        // binds here; if it isn't, Kestrel falls back to the default and the test fails.
        const int SentinelPort = 58997;
        var configPath = Path.Combine(configDir, "appsettings.json");
        var configContents = $$"""
        {
          "Hosting": {
            "ListenPort": {{SentinelPort}}
          }
        }
        """;

        await File.WriteAllTextAsync(configPath, configContents);

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

            var tempDataPath = Path.Combine(alienCwd, "data");
            var tempUserTypesPath = Path.Combine(alienCwd, "usertypes");
            psi.Environment["COLLABHOST_DATA_PATH"] = tempDataPath;
            psi.Environment["COLLABHOST_USER_TYPES_PATH"] = tempUserTypesPath;
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
            psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
            psi.Environment["COLLABHOST_CONFIG_PATH"] = configPath;

            using var process = Process.Start(psi);

            process.ShouldNotBeNull();

            using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var observedListeningUrl = await ReadListeningUrlAsync(process!, readCts.Token);

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

            observedListeningUrl.ShouldNotBeNullOrWhiteSpace();

            observedListeningUrl!.ShouldContain
            (
                SentinelPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Case.Insensitive,
                $"Kestrel should bind to the sentinel port from COLLABHOST_CONFIG_PATH. observed='{observedListeningUrl}'"
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

            try
            {
                Directory.Delete(configDir, true);
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

    private static async Task<string?> ReadListeningUrlAsync(Process process, CancellationToken ct)
    {
        // Microsoft.Hosting.Lifetime writes "Now listening on: <url>" once Kestrel has bound.
        // Read until that line appears or the token cancels.
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
}
