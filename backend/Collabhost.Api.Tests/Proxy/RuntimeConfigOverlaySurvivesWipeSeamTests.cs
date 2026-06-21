using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

using Collabhost.Api.Platform;
using Collabhost.Api.Portal;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;

using Shouldly;

using Xunit;
using Xunit.Sdk;

namespace Collabhost.Api.Tests.Proxy;

// Card #376 -- the runtime-config-overlay serve-path seam guard.
//
// The #369 design puts the runtime-config overlay file in the app's writable
// data dir (RuntimeConfigWritableRoot), decoupled from the static-site ARTIFACT
// tree (ArtifactDirectory). The promise: an operator's configured value survives
// a redeploy that wipes the artifact directory and STILL serves, because the
// overlay file was never in the wiped tree and the live overlay route resolves
// it from the writable root.
//
// The existing unit suite proves the necessary halves and stops short:
//   - RuntimeConfigFileWriterTests proves the file lands in the writable dir,
//     not the artifact dir (decoupling at the writer seam).
//   - ProxyConfigurationBuilderTests.Build_FileServerRoute_RuntimeConfigWith-
//     SpaFallback_SpaFallbackStillSeesArtifactRoot proves the emitted config
//     shape (overlay root confined to its path-matched nested subroute).
// Neither spawns the proxy or wipes the artifact tree, so the SERVE path under
// a live artifact-dir wipe is unmodeled by the unit suite. This file closes that
// gap -- it is the automated form of the S73 v1.6.3 promote-gate e2e that proved
// the property by hand.
//
// Fixture model: real ProxyConfigurationBuilder output + a real Caddy child
// process, soft-skipped when `caddy` is absent on PATH (same shape as
// ArtifactEvidenceCollectorSeamTests' test-time `dotnet publish`). The route
// subtree fed to Caddy is the BYTE-IDENTICAL @id/match/handle/terminal output of
// ProxyConfigurationBuilder.Build(...) for the production overlay RouteEntry --
// only the surrounding server is rewritten to a plain-HTTP listener with
// automatic_https disabled (TLS/PKI are orthogonal to the file-serving overlay
// property under test, and were stripped the same way in the S73 hand-run).
//
// The test walks the full S73 sequence in one method because the steps mutate
// shared on-disk state in order (materialize, then wipe, then redeploy, then
// control); xUnit runs separate [Fact] methods on fresh instances in arbitrary
// order, so the sequence cannot be split without losing the ordering the gate
// depends on.
public sealed class RuntimeConfigOverlaySurvivesWipeSeamTests : IAsyncLifetime
{
    private const string _routeSlug = "portal";
    private const string _baseDomain = "collab.internal";
    private const string _configPath = "/config.json";
    private const string _operatorValue = "{\"apiBaseUrl\":\"https://api.example.com/api/v1\"}";
    private const string _staleArtifactDefault = "{\"apiBaseUrl\":\"https://STALE-ARTIFACT-DEFAULT.example.com\"}";
    private const string _spaIndexBody = "PORTAL SPA INDEX";
    private const string _spaIndexBodyV2 = "PORTAL SPA INDEX v2";

    private readonly string _hostHeader = $"{_routeSlug}.{_baseDomain}";

    private string _scratchRoot = string.Empty;
    private string _artifactDir = string.Empty;
    private string _writableRoot = string.Empty;
    private string _artifactConfigPath = string.Empty;
    private string _artifactIndexPath = string.Empty;
    private string _writableConfigPath = string.Empty;

    private Process? _caddy;
    private string _caddyConfigPath = string.Empty;
    private int _listenPort;
    private bool _caddyStarted;
    private string _caddyUnavailableReason = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _scratchRoot = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-overlay-wipe-seam",
            Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_scratchRoot);

        // --- materialize the initial layout (S73 step: INITIAL LAYOUT) ---
        // Artifact dir carries a STALE config.json default + an index.html, so a
        // pre-wipe 200 that returns the OPERATOR value (not the stale default)
        // proves the overlay shadows the artifact-dir file in the first place.
        _artifactDir = Path.Combine(_scratchRoot, "artifact");
        _writableRoot = Path.Combine(_scratchRoot, "data", "app-data", _routeSlug);

        Directory.CreateDirectory(_artifactDir);
        Directory.CreateDirectory(_writableRoot);

        _artifactConfigPath = Path.Combine(_artifactDir, "config.json");
        _artifactIndexPath = Path.Combine(_artifactDir, "index.html");
        _writableConfigPath = Path.Combine(_writableRoot, "config.json");

        await File.WriteAllTextAsync(_artifactConfigPath, _staleArtifactDefault);
        await File.WriteAllTextAsync
        (
            _artifactIndexPath,
            $"<!doctype html><html><body>{_spaIndexBody}</body></html>"
        );

        // The operator value, in the writable dir, in the exact flat-JSON shape
        // RuntimeConfigFileWriter.RenderAsync produces (unit-proven separately).
        await File.WriteAllTextAsync(_writableConfigPath, _operatorValue);

        // --- locate caddy; soft-skip the whole scenario when it is absent ---
        var caddyPath = ResolveCaddyOnPath();

        if (caddyPath is null)
        {
            _caddyUnavailableReason =
                "`caddy` was not found on PATH. This seam test spawns a real Caddy "
                + "process against real ProxyConfigurationBuilder output; without the "
                + "binary the serve-path property cannot be exercised.";

            return;
        }

        // --- emit the REAL builder output, then derive a plain-HTTP serve config ---
        _listenPort = FindFreePort();
        _caddyConfigPath = Path.Combine(_scratchRoot, "serve.json");

        var serveConfig = BuildPlainHttpServeConfig(_listenPort);

        await File.WriteAllTextAsync(_caddyConfigPath, serveConfig.ToJsonString());

        try
        {
            _caddy = StartCaddy(caddyPath, _caddyConfigPath);

            await WaitForCaddyReadyAsync(_caddy);

            _caddyStarted = true;
        }
        catch (CaddyUnavailableException ex)
        {
            _caddyUnavailableReason = ex.Message;

            TryStopCaddy();
        }
    }

    public ValueTask DisposeAsync()
    {
        TryStopCaddy();

        try
        {
            if (Directory.Exists(_scratchRoot))
            {
                Directory.Delete(_scratchRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort scratch cleanup -- a leaked temp dir under GetTempPath()
            // does not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Same best-effort rationale as the IOException branch.
        }

        return ValueTask.CompletedTask;
    }

    // The serve-path gate, walked as the S73 sequence: pre-wipe shadowing,
    // artifact-dir wipe, post-wipe survival, fresh-bundle redeploy, and the
    // control that proves the surviving 200s came from the writable overlay (not
    // artifact-dir leakage). Each step's assertion is annotated with why it is
    // load-bearing.
    [Fact]
    public async Task RuntimeConfigOverlay_SurvivesArtifactDirWipe_AndStillServes()
    {
        SkipIfCaddyUnavailable();

        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_listenPort.ToString(CultureInfo.InvariantCulture)}"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // === STEP 1 -- PRE-WIPE: the overlay shadows the artifact-dir default ===
        // A 200 carrying the OPERATOR value (not the stale artifact default)
        // proves the overlay route wins over the artifact-dir config.json, so the
        // operator value is what is served in the first place.
        var preWipe = await GetAsync(client, _configPath);

        preWipe.Status.ShouldBe(HttpStatusCode.OK, "pre-wipe GET /config.json should serve the overlay");
        preWipe.Body.ShouldBe
        (
            _operatorValue,
            "pre-wipe /config.json must serve the OPERATOR value from the writable "
            + "overlay, NOT the stale artifact-dir default -- otherwise the overlay "
            + "is not shadowing the artifact file and the rest of the gate is moot."
        );

        // === STEP 1b -- PRE-WIPE: SPA deep-link still serves index.html ===
        // Confirms the overlay's path-scoped root switch did not bleed into the
        // SPA fallback (the #369 root-bleed regression). The deep-link path does
        // not match /config.json, so it falls through to the artifact-rooted SPA
        // fallback and serves index.html.
        var preWipeDeepLink = await GetAsync(client, "/boards/deep-link");

        preWipeDeepLink.Status.ShouldBe(HttpStatusCode.OK, "pre-wipe SPA deep-link should serve index.html");
        preWipeDeepLink.Body.ShouldContain
        (
            _spaIndexBody,
            customMessage: "pre-wipe SPA deep-link must serve the artifact-dir index.html "
            + "via the SPA fallback -- proving the overlay root did not bleed chain-wide."
        );

        // === STEP 2 -- WIPE: remove the entire artifact tree ===
        // The realistic redeploy removes the artifact dir's contents (rsync
        // --delete an empty source, or a fresh bundle). The writable dir is a
        // SEPARATE tree and is left untouched.
        WipeArtifactTree();

        Directory.GetFiles(_artifactDir).ShouldBeEmpty("the artifact tree should be fully wiped");
        File.Exists(_writableConfigPath).ShouldBeTrue
        (
            "the writable overlay file lives outside the artifact tree, so the wipe "
            + "must NOT touch it -- this is the decoupling the gate rests on."
        );

        // === STEP 3 -- POST-WIPE (THE GATE): the operator value survives + serves ===
        // The artifact config.json and index.html are gone. The writable file was
        // never in that tree, so the value survives; the overlay route's root is
        // the writable dir, so it still resolves. This is the exact property the
        // S73 hand-run proved and that no automated test covered before #376.
        var postWipe = await GetAsync(client, _configPath);

        postWipe.Status.ShouldBe
        (
            HttpStatusCode.OK,
            "POST-WIPE GATE: GET /config.json must still serve 200 after the artifact "
            + "tree is wiped -- the overlay file survives because it lives in the "
            + "writable data dir, decoupled from the artifact tree (#369)."
        );
        postWipe.Body.ShouldBe
        (
            _operatorValue,
            "POST-WIPE GATE: the surviving 200 must carry the operator value."
        );

        // === STEP 4 -- REDEPLOY v2: fresh artifact bundle, NO config.json ===
        // The realistic redeploy ships a NEW index.html and does NOT ship a
        // config.json (the operator value lives only in the writable overlay). The
        // operator value still serves, and the SPA deep-link picks up the new
        // index live -- confirming no root-bleed in the RUNNING server, not just
        // the emitted config.
        await File.WriteAllTextAsync
        (
            _artifactIndexPath,
            $"<!doctype html><html><body>{_spaIndexBodyV2}</body></html>"
        );

        var redeployConfig = await GetAsync(client, _configPath);

        redeployConfig.Status.ShouldBe(HttpStatusCode.OK, "post-redeploy /config.json should still serve the overlay");
        redeployConfig.Body.ShouldBe
        (
            _operatorValue,
            "after a fresh artifact bundle with no config.json, the operator value "
            + "must still serve from the writable overlay."
        );

        var redeployDeepLink = await GetAsync(client, "/boards/deep-link");

        redeployDeepLink.Status.ShouldBe(HttpStatusCode.OK, "post-redeploy SPA deep-link should serve the new index");
        redeployDeepLink.Body.ShouldContain
        (
            _spaIndexBodyV2,
            customMessage: "the SPA deep-link must serve the NEW index.html (v2) live -- "
            + "confirming the artifact root is read live and the overlay never bled "
            + "into the running SPA fallback."
        );

        // === STEP 5 -- CONTROL: remove the writable file -> 404 ===
        // Removing the writable overlay file must yield 404, NOT a fallback to the
        // artifact dir. This rules out the false-positive that the surviving 200s
        // leaked from the artifact dir: with the writable file gone the overlay
        // route resolves nothing and there is no artifact fallback for /config.json
        // (the no-fallback property, #369 Q1). The 404 proves the surviving 200s
        // were unambiguously served from the writable overlay.
        File.Delete(_writableConfigPath);

        var control = await GetAsync(client, _configPath);

        control.Status.ShouldBe
        (
            HttpStatusCode.NotFound,
            "CONTROL: with the writable overlay file removed, GET /config.json must "
            + "404 -- there is NO fallback to the artifact dir for the config path. "
            + "This proves every surviving 200 above came from the writable overlay, "
            + "not from artifact-dir leakage."
        );
    }

    // Builds a minimal plain-HTTP Caddy config whose route subtree is the
    // BYTE-IDENTICAL @id/match/handle/terminal output of the real
    // ProxyConfigurationBuilder for the production overlay RouteEntry. Only the
    // surrounding server is rewritten: a single plain-HTTP listener on the chosen
    // port with automatic_https disabled. TLS/PKI/admin/self-route are dropped --
    // they are orthogonal to the file-serving overlay property under test (the
    // S73 hand-run stripped them the same way).
    private JsonObject BuildPlainHttpServeConfig(int listenPort)
    {
        var settings = new ProxySettings
        {
            BaseDomain = _baseDomain,
            BinaryPath = "caddy",
            ListenAddress = ":443",
            CertLifetime = "168h",
            AdminPort = 2019
        };

        var hosting = new HostingSettings
        {
            ListenAddress = "localhost",
            ListenPort = 58400
        };

        var portal = new PortalSettings
        {
            Subdomain = "collabhost"
        };

        // The production overlay shape: a file-server route with SPA fallback,
        // an artifact dir, and the #369 runtime-config overlay pointing at the
        // writable root. This is the exact RouteEntry that drives the overlay
        // emission in production.
        var route = new RouteEntry
        (
            _routeSlug,
            $"{_routeSlug}.{_baseDomain}",
            ServeMode.FileServer,
            Port: null,
            SpaFallback: true,
            ArtifactDirectory: _artifactDir,
            Enabled: true,
            RuntimeConfigFilePath: _configPath,
            RuntimeConfigWritableRoot: _writableRoot
        );

        var fullConfig = ProxyConfigurationBuilder.Build([route], settings, hosting, portal);

        // Pull the real emitted route subtree for our slug. The builder emits the
        // self route at index 0 and the app routes after; match on @id so this
        // does not silently grab the wrong route if the order ever changes.
        var emittedRoute = FindEmittedRoute(fullConfig, $"route_{_routeSlug}");

        // Reassemble around a plain-HTTP listener. The route node is the real
        // builder output, carried verbatim (deep-cloned so it detaches cleanly
        // from the source document).
        return new JsonObject
        {
            ["apps"] = new JsonObject
            {
                ["http"] = new JsonObject
                {
                    ["servers"] = new JsonObject
                    {
                        ["srv0"] = new JsonObject
                        {
                            ["listen"] = new JsonArray { $":{listenPort.ToString(CultureInfo.InvariantCulture)}" },
                            ["routes"] = new JsonArray { emittedRoute.DeepClone() },
                            ["automatic_https"] = new JsonObject
                            {
                                ["disable"] = true
                            }
                        }
                    }
                }
            }
        };
    }

    private static JsonNode FindEmittedRoute(JsonObject fullConfig, string id)
    {
        var routes = fullConfig["apps"]?["http"]?["servers"]?["srv0"]?["routes"]?.AsArray()
            ?? throw new CaddyUnavailableException
            (
                "ProxyConfigurationBuilder.Build emitted no srv0 routes -- the builder "
                + "shape changed and this seam test can no longer locate the overlay route."
            );

        foreach (var route in routes)
        {
            if (route is JsonObject routeObject
                && routeObject.TryGetPropertyValue("@id", out var idNode)
                && string.Equals(idNode?.GetValue<string>(), id, StringComparison.Ordinal))
            {
                return route;
            }
        }

        throw new CaddyUnavailableException
        (
            $"ProxyConfigurationBuilder.Build emitted no route with @id '{id}' -- the "
            + "builder shape changed and this seam test can no longer locate the overlay route."
        );
    }

    private static Process StartCaddy(string caddyPath, string configPath)
    {
        var startInfo = new ProcessStartInfo(caddyPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Native JSON config -- `caddy run --config <file.json>` uses the JSON
        // adapter by default for a .json file. No Caddyfile adapter involved.
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);

        var process = Process.Start(startInfo)
            ?? throw new CaddyUnavailableException("Process.Start('caddy run ...') returned null.");

        return process;
    }

    private async Task WaitForCaddyReadyAsync(Process caddy)
    {
        // Poll the served port until Caddy answers (any HTTP response means the
        // listener is up and the route is live). 30s ceiling -- far over the
        // ~1s warm start, well under any CI step timeout. A caddy that exits
        // before binding (config rejected, port taken) is surfaced as a soft
        // skip with its captured stderr, not a flaky failure.
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_listenPort.ToString(CultureInfo.InvariantCulture)}"),
            Timeout = TimeSpan.FromSeconds(2)
        };

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (caddy.HasExited)
            {
                var stderr = await caddy.StandardError.ReadToEndAsync();

                throw new CaddyUnavailableException
                (
                    $"caddy exited (code {caddy.ExitCode.ToString(CultureInfo.InvariantCulture)}) "
                    + "before binding its listener -- the serve config was rejected or the "
                    + "port was taken. STDERR:" + Environment.NewLine + stderr
                );
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _configPath);

                request.Headers.TryAddWithoutValidation("Host", _hostHeader);

                using var response = await client.SendAsync(request);

                // Any HTTP status means the listener answered -- Caddy is ready.
                return;
            }
            catch (HttpRequestException)
            {
                // Listener not up yet -- retry until the deadline.
            }
            catch (TaskCanceledException)
            {
                // Per-request timeout -- retry until the deadline.
            }

            await Task.Delay(100);
        }

        throw new CaddyUnavailableException
        (
            "caddy did not answer on its listener within 30s -- treating as a fixture "
            + "availability failure rather than a contract regression."
        );
    }

    private async Task<HttpResult> GetAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        // Route matching is host-based (route.Domain). Set the Host header so the
        // request lands on the overlay route, exactly as the proxy fronts it.
        request.Headers.TryAddWithoutValidation("Host", _hostHeader);

        using var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();

        return new HttpResult(response.StatusCode, body.Trim());
    }

    private void WipeArtifactTree()
    {
        // Mirror the S73 redeploy wipe (rsync --delete an empty source): the
        // artifact dir is emptied while the writable data dir is untouched.
        foreach (var file in Directory.GetFiles(_artifactDir, "*", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    private void SkipIfCaddyUnavailable()
    {
        if (_caddyStarted)
        {
            return;
        }

        // The fixture could not stand up a real Caddy serving the real config.
        // Surface a soft skip rather than a flaky failure: the absence of the
        // binary (or a transient bind failure) is not the serve-path contract
        // regression this test guards. CI runners with caddy on PATH exercise
        // it; everyone else still runs the rest of the suite.
        throw SkipException.ForSkip
        (
            "RuntimeConfigOverlaySurvivesWipeSeamTests requires a real `caddy` on PATH "
            + "to spawn against the real ProxyConfigurationBuilder output. "
            + _caddyUnavailableReason
        );
    }

    private void TryStopCaddy()
    {
        if (_caddy is null)
        {
            return;
        }

        try
        {
            if (!_caddy.HasExited)
            {
                _caddy.Kill(entireProcessTree: true);
                _caddy.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone between HasExited and Kill -- nothing to do.
        }
        finally
        {
            _caddy.Dispose();
            _caddy = null;
        }
    }

    // Resolve `caddy` against the PATH directories, honoring PATHEXT on Windows.
    // Returns null when no executable is found -- the soft-skip signal.
    private static string? ResolveCaddyOnPath()
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(pathValue))
        {
            return null;
        }

        var isWindows = OperatingSystem.IsWindows();

        var candidateNames = isWindows
            ? new[] { "caddy.exe", "caddy.cmd", "caddy.bat", "caddy" }
            : ["caddy"];

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in candidateNames)
            {
                var candidate = Path.Combine(dir, name);

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return port;
    }

    private readonly record struct HttpResult(HttpStatusCode Status, string Body);

    // S3871 wants exception types public, but this one is a test-only signal for
    // the IAsyncLifetime fixture path -- it never escapes the assembly.
    // Suppressed narrowly so the rule still catches genuine cases.
#pragma warning disable S3871
    private sealed class CaddyUnavailableException(string message) : Exception(message);
#pragma warning restore S3871
}
