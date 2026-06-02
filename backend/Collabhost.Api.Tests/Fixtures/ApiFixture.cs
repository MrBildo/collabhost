using System.Text.Json.Nodes;

using Collabhost.Api.Platform;
using Collabhost.Api.Proxy;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Collabhost.Api.Tests.Fixtures;

public class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string? _dbDirectory;
    private string? _userTypesDirectory;
    private string? _wwwrootDirectory;

    public const string AdminKey = "01INTEG0TEST0KEY00000000";

    public HttpClient Client { get; private set; } = null!;

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateClient() => _factory.CreateClient();

    public string WwwrootDirectory =>
        _wwwrootDirectory
            ?? throw new InvalidOperationException("ApiFixture not initialized.");

    public async Task InitializeAsync()
    {
        // Null the env vars so a developer shell -- or a leak from another test -- does not
        // shadow the UseSetting paths below. Each of these wins over config (env > config) in
        // the resolvers Program.cs / DataRegistration / TypeStoreRegistration use, so without
        // the explicit null the host would silently bind its data dir / content root / user-types
        // dir to the inherited value instead of this fixture's per-test temp dirs. The
        // COLLABHOST_DATA_PATH / ASPNETCORE_CONTENTROOT nulls also defend against the #354/#378
        // pollution flake (UpdateHostsCliTests leaks both, pointing at a torn-down scratch dir).
        Environment.SetEnvironmentVariable("COLLABHOST_USER_TYPES_PATH", null);
        Environment.SetEnvironmentVariable("COLLABHOST_DATA_PATH", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", null);

        _dbDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_dbDirectory);

        // Isolated per-test user-types directory. Preflight creates it if missing; we want a
        // clean slate so test runs don't leave a stray 'UserTypes' dir next to the test binaries.
        _userTypesDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests-usertypes", Guid.NewGuid().ToString("N")
        );

        // Per-test temp wwwroot. Seeded with index.html and assets/seeded.js so
        // PortalReachabilityCheck reports Ok and the SPA-fallback middleware finds
        // a real shell to serve. Tests that exercise the missing-shell degraded mode
        // delete the seeded files at the start of the test (see Portal middleware tests).
        _wwwrootDirectory = Path.Combine
        (
            Path.GetTempPath(), "collabhost-api-tests-wwwroot", Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(Path.Combine(_wwwrootDirectory, "assets"));
        await File.WriteAllTextAsync
        (
            Path.Combine(_wwwrootDirectory, "index.html"),
            "<!doctype html><html><body data-test=\"seeded-shell\"></body></html>"
        );
        await File.WriteAllTextAsync
        (
            Path.Combine(_wwwrootDirectory, "assets", "seeded.js"),
            "// seeded\n"
        );

        var dbPath = Path.Combine(_dbDirectory, "collabhost.db");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder
            (
                builder =>
                {
                    builder.UseSetting("ConnectionStrings:Host", $"Data Source={dbPath}");
                    builder.UseSetting("Auth:AdminKey", AdminKey);
                    builder.UseSetting("TypeStore:UserTypesDirectory", _userTypesDirectory);
                    builder.UseSetting("Proxy:BaseDomain", "test.internal");
                    builder.UseSetting("Proxy:AdminApiUrl", "http://localhost:29999");
                    builder.UseSetting("Proxy:BinaryPath", "caddy");
                    builder.UseSetting("Proxy:ListenAddress", ":443");
                    builder.UseSetting("Proxy:CertLifetime", "168h");
                    builder.UseSetting("Hosting:ListenPort", "58400");
                    builder.UseSetting(WebHostDefaults.WebRootKey, _wwwrootDirectory);

                    // Suppress verbose EF Core SQL and ASP.NET Core info logging during tests.
                    // When dotnet test runs multiple projects concurrently, high-volume console
                    // output deadlocks the .NET 10 terminal logger's pipe buffer on Linux.
                    builder.UseSetting("Logging:LogLevel:Default", "Warning");

                    builder.ConfigureServices
                    (
                        services =>
                        {
                            // Replace the Caddy client with a no-op fake
                            services.AddSingleton<ICaddyClient, FakeCaddyClient>();

                            // Suppress the .last-boot-version sentinel write under
                            // WebApplicationFactory<Program>. ApplicationStarted fires per test,
                            // so without this the temp data directory gets a stray sentinel
                            // (PR #95 MED-2).
                            services.AddSingleton<IBootVersionWriter, NoopBootVersionWriter>();
                        }
                    );
                }
            );

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();

        await _factory.DisposeAsync();

        if (_dbDirectory is not null && Directory.Exists(_dbDirectory))
        {
            try
            {
                Directory.Delete(_dbDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_userTypesDirectory is not null && Directory.Exists(_userTypesDirectory))
        {
            try
            {
                Directory.Delete(_userTypesDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        if (_wwwrootDirectory is not null && Directory.Exists(_wwwrootDirectory))
        {
            try
            {
                Directory.Delete(_wwwrootDirectory, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }
}

// Sealed: file-scoped test fake, no inheritance needed
file sealed class FakeCaddyClient : ICaddyClient
{
    public Task<bool> IsReadyAsync(CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<LoadConfigResult> LoadConfigAsync(JsonObject config, CancellationToken ct = default) =>
        Task.FromResult(LoadConfigResult.Ok());

    public Task<JsonObject?> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult<JsonObject?>(null);
}

// Sealed: file-scoped test fake, no inheritance needed
file sealed class NoopBootVersionWriter : IBootVersionWriter
{
    public void Write(string dataDirectory, string version)
    {
        // No-op: integration tests should not leave a .last-boot-version sentinel behind.
    }
}

// Isolation invariant for env-var poisoners (#354/#378):
//
// A test that sets COLLABHOST_DATA_PATH or ASPNETCORE_CONTENTROOT MUST join [Collection("Api")]
// IF it creates-then-deletes the directory those vars point at. Both vars win over UseSetting in a
// host boot (env > config), so a concurrent ApiFixture/WebApplicationFactory<Program> boot -- or a
// subprocess launcher whose child inherits the parent env -- resolves its data dir / content root to
// the poisoner's scratch dir and then races the teardown delete. That create-then-DELETE TOCTOU is
// the flake. Co-membership in this collection is the only thing that serializes a poisoner against
// those boots: xUnit runs collections in PARALLEL by default, and DisableParallelization on a
// *separate* collection serializes only within that collection, not across to this one.
//
// Poisoners that set the same two vars but point ONLY at static, never-deleted paths are safe to run
// in parallel and correctly stay OUT of this collection. They fail DETERMINISTICALLY, not flakily: a
// concurrent boot that resolved, say, /var/lib/collabhost/data would hit a StartupPreflight halt
// (non-writable path -> attributable error), never the intermittent dir-vanishes-mid-boot crash. The
// three pre-existing same-var poisoners outside this collection are each safe on that basis --
// ChildProcessEnvironmentTests and *ProcessRunnerEnvironmentIsolationTests (#330, /opt/collabhost +
// /var/lib/collabhost/data) and DataRegistrationTests (Path.GetTempPath(), which is writable -> a
// concurrent boot resolving it is simply harmless). Their non-coverage is documented at the source
// in EnvironmentPoison.cs. Do NOT "fix" them by adding them to this collection -- that needlessly
// serializes the #330 isolation suite against every Api boot for no race. The discriminator is
// create-then-delete, not "touches the same var."
[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<ApiFixture> { }
