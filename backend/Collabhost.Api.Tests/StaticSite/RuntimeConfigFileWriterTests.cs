using System.Text.Json.Nodes;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Data;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Proxy;
using Collabhost.Api.Registry;
using Collabhost.Api.StaticSite;
using Collabhost.Api.Tests.Fixtures;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.StaticSite;

// Tests for the writer service introduced by Card #336. The primary contract
// under test is the EMPTY-VALUES INVARIANT: when the resolved Values dict is
// empty the writer makes ZERO filesystem calls -- a hand-edited config.json on
// disk must survive a Collabhost upgrade where the new runtime-config-file
// capability default ships as empty values. This is load-bearing for CLAUDE.md
// Rule 3 (no silent overwrite of operator-maintained content).
//
// Three migration states all converge to the empty-values branch:
//   1. No CapabilityOverride row at all (default empty {} resolves to {}).
//   2. Override row exists but contains no `values` key.
//   3. Override row contains `values: {}` (operator deleted all entries).
//
// Each is exercised below. Delete-after-write (Bill ruling S55 #6: option (b))
// is verified by the empty-values-after-write test -- writer no-op, prior file
// stays.
public class RuntimeConfigFileWriterTests : IAsyncLifetime, IDisposable
{
    private TypeStore _typeStore = null!;
    private CapabilityStore _capabilityStore = null!;
    private AppStore _appStore = null!;
    private RuntimeConfigFileWriter _writer = null!;
    private SqliteConnection _connection = null!;
    private string _tempArtifactDir = null!;
    private string _tempDataDir = null!;
    private AppDataPathResolver _dataPathResolver = null!;

    public async ValueTask InitializeAsync()
    {
        _typeStore = new TypeStore
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = Path.Combine(Path.GetTempPath(), "collabhost-test-notexist") },
            new ProxySettings { BaseDomain = "collab.internal", BinaryPath = "caddy", ListenAddress = ":443", CertLifetime = "168h" },
            new StubHostEnvironment(),
            NullLogger<TypeStore>.Instance
        );

        await _typeStore.LoadAsync();

        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbFactory = new InMemoryDbContextFactory(_connection);

        await using var context = await dbFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        _appStore = new AppStore
        (
            dbFactory,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AppStore>.Instance
        );

        _capabilityStore = new CapabilityStore
        (
            _typeStore,
            _appStore,
            NullLogger<CapabilityStore>.Instance
        );

        _tempDataDir = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-test-rcf-data-" + Guid.NewGuid().ToString("N")
        );

        _dataPathResolver = new AppDataPathResolver(_tempDataDir);

        _writer = new RuntimeConfigFileWriter
        (
            _capabilityStore,
            _dataPathResolver,
            NullLogger<RuntimeConfigFileWriter>.Instance
        );

        _tempArtifactDir = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-test-rcf-" + Guid.NewGuid().ToString("N")
        );

        Directory.CreateDirectory(_tempArtifactDir);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        foreach (var dir in new[] { _tempArtifactDir, _tempDataDir })
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; tests should not fail on temp-dir teardown.
                }
            }
        }

        return _connection.DisposeAsync();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<App> CreateStaticSiteAppAsync()
    {
        var app = await _appStore.CreateAsync
        (
            new App
            {
                Slug = "test-site",
                DisplayName = "Test Site",
                AppTypeSlug = "static-site"
            },
            CancellationToken.None
        );

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "artifact",
            $$"""{"location":"{{_tempArtifactDir.Replace("\\", "\\\\", StringComparison.Ordinal)}}"}""",
            CancellationToken.None
        );

        return app;
    }

    [Fact]
    public async Task RenderAsync_EmptyValues_NoOverrideRow_DoesNotTouchDisk()
    {
        // Migration state 1: no override row at all -- type default {} resolves
        // to empty values. Hand-edited file on disk must survive.
        var app = await CreateStaticSiteAppAsync();

        var targetPath = Path.Combine(_tempArtifactDir, "config.json");
        const string operatorContent = """{"apiBaseUrl":"https://hand-edited.example.com"}""";

        await File.WriteAllTextAsync(targetPath, operatorContent, CancellationToken.None);

        await _writer.RenderAsync(app, CancellationToken.None);

        var contentAfter = await File.ReadAllTextAsync(targetPath, CancellationToken.None);

        contentAfter.ShouldBe(operatorContent);
    }

    [Fact]
    public async Task RenderAsync_EmptyValues_OverrideRowMissingValuesKey_DoesNotTouchDisk()
    {
        // Migration state 2: override row exists but carries only a path edit
        // -- resolved values fall through to default {} and the writer must
        // not touch disk.
        var app = await CreateStaticSiteAppAsync();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"path":"/config.json"}""",
            CancellationToken.None
        );

        var targetPath = Path.Combine(_tempArtifactDir, "config.json");
        const string operatorContent = """{"apiBaseUrl":"https://hand-edited.example.com"}""";

        await File.WriteAllTextAsync(targetPath, operatorContent, CancellationToken.None);

        await _writer.RenderAsync(app, CancellationToken.None);

        var contentAfter = await File.ReadAllTextAsync(targetPath, CancellationToken.None);

        contentAfter.ShouldBe(operatorContent);
    }

    [Fact]
    public async Task RenderAsync_EmptyValues_OverrideRowWithEmptyValuesObject_DoesNotTouchDisk()
    {
        // Migration state 3: operator typed values then deleted them all,
        // leaving values: {} in the override. Writer must no-op (delete-after-
        // write semantic (b), Bill ruling S55 #6) -- the file on disk from any
        // prior write stays as-is; platform does not silently delete operator-
        // visible artifacts.
        var app = await CreateStaticSiteAppAsync();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"values":{}}""",
            CancellationToken.None
        );

        var targetPath = Path.Combine(_tempArtifactDir, "config.json");
        const string priorWriteContent = """{"apiBaseUrl":"https://prior-write.example.com"}""";

        await File.WriteAllTextAsync(targetPath, priorWriteContent, CancellationToken.None);

        await _writer.RenderAsync(app, CancellationToken.None);

        var contentAfter = await File.ReadAllTextAsync(targetPath, CancellationToken.None);

        contentAfter.ShouldBe(priorWriteContent);
    }

    [Fact]
    public async Task RenderAsync_EmptyValues_NoFileOnDisk_DoesNotCreateFile()
    {
        // Empty-values + no file on disk: writer is a no-op. Tests the "never
        // adopted" migration state -- operator never used the feature, no file
        // ever gets created by the platform.
        var app = await CreateStaticSiteAppAsync();

        var targetPath = Path.Combine(_tempArtifactDir, "config.json");

        File.Exists(targetPath).ShouldBeFalse();

        await _writer.RenderAsync(app, CancellationToken.None);

        File.Exists(targetPath).ShouldBeFalse();
    }

    [Fact]
    public async Task RenderAsync_NonEmptyValues_WritesToWritableDataDir_NotArtifactDir()
    {
        // #369: the writer renders into the app's writable data dir, NOT the
        // (read-only on a Standard install) artifact dir. This is the seam test
        // whose ABSENCE let #336 ship the EROFS defect.
        var app = await CreateStaticSiteAppAsync();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"values":{"apiBaseUrl":"https://api.example.com"}}""",
            CancellationToken.None
        );

        await _writer.RenderAsync(app, CancellationToken.None);

        var writableTarget = _dataPathResolver.ResolveFor(app.Slug);
        var targetPath = Path.Combine(writableTarget, "config.json");

        var json = await File.ReadAllTextAsync(targetPath, CancellationToken.None);
        var parsed = JsonNode.Parse(json)!.AsObject();

        parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://api.example.com");

        // The artifact dir is never written.
        File.Exists(Path.Combine(_tempArtifactDir, "config.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task RenderAsync_NonEmptyValues_CreatesWritableSubtree()
    {
        // #369 dir-ensure: {dataRoot}/app-data/{slug}/ is not created by anything
        // else on the platform; the writer must create it before the atomic write.
        var app = await CreateStaticSiteAppAsync();

        var writableTarget = _dataPathResolver.ResolveFor(app.Slug);
        Directory.Exists(writableTarget).ShouldBeFalse();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"values":{"apiBaseUrl":"https://api.example.com"}}""",
            CancellationToken.None
        );

        await _writer.RenderAsync(app, CancellationToken.None);

        Directory.Exists(writableTarget).ShouldBeTrue();
        File.Exists(Path.Combine(writableTarget, "config.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task RenderAsync_NonEmptyValues_OverwritesPriorWritableFile()
    {
        // Once the operator types values the platform takes over -- subsequent
        // write replaces any prior content at the writable target. Tests the
        // deployment-takes-over-by-explicit-action shape against the #369
        // writable-dir target.
        var app = await CreateStaticSiteAppAsync();

        var writableTarget = _dataPathResolver.ResolveFor(app.Slug);
        Directory.CreateDirectory(writableTarget);

        var targetPath = Path.Combine(writableTarget, "config.json");

        await File.WriteAllTextAsync
        (
            targetPath,
            """{"apiBaseUrl":"https://stale.example.com"}""",
            CancellationToken.None
        );

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"values":{"apiBaseUrl":"https://fresh.example.com"}}""",
            CancellationToken.None
        );

        await _writer.RenderAsync(app, CancellationToken.None);

        var json = await File.ReadAllTextAsync(targetPath, CancellationToken.None);
        var parsed = JsonNode.Parse(json)!.AsObject();

        parsed["apiBaseUrl"]!.GetValue<string>().ShouldBe("https://fresh.example.com");
    }

    [Fact]
    public async Task RenderAsync_NonEmptyValues_ArtifactDirMissing_StillWritesToWritableDir()
    {
        // #369: the writer no longer gates on the artifact dir. Whether the
        // served files are deployed is route-enable's concern, not the writer's.
        // The write lands under the writable data dir regardless of the artifact
        // dir's state. This is the cross-platform-safe core of the seam test --
        // the artifact dir being missing (or read-only on Linux) does not block
        // the write, because the write never touches it.
        var app = await CreateStaticSiteAppAsync();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "artifact",
            """{"location":"/nonexistent/collabhost/test/path/that/should/not/exist"}""",
            CancellationToken.None
        );

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "runtime-config-file",
            """{"values":{"apiBaseUrl":"https://api.example.com"}}""",
            CancellationToken.None
        );

        await Should.NotThrowAsync
        (
            () => _writer.RenderAsync(app, CancellationToken.None)
        );

        var targetPath = Path.Combine(_dataPathResolver.ResolveFor(app.Slug), "config.json");
        File.Exists(targetPath).ShouldBeTrue();
    }

    [Fact]
    public async Task RenderAsync_EmptyValues_ArtifactDirMissing_DoesNotThrow()
    {
        // Empty values + missing artifact dir is fine -- the no-op branch fires
        // before any path resolution. The operator never opted in; the platform
        // never tries to write.
        var app = await CreateStaticSiteAppAsync();

        await _appStore.SaveOverrideAsync
        (
            app.Id,
            "artifact",
            """{"location":"/nonexistent/collabhost/test/path"}""",
            CancellationToken.None
        );

        await Should.NotThrowAsync
        (
            () => _writer.RenderAsync(app, CancellationToken.None)
        );
    }

    [Fact]
    public async Task RenderAsync_CapabilityNotDeclared_NoOps()
    {
        // dotnet-app type does not declare runtime-config-file. The writer
        // returns silently when the capability is absent -- callers should be
        // free to invoke it on any app without first inspecting the type.
        var app = await _appStore.CreateAsync
        (
            new App
            {
                Slug = "dotnet-test",
                DisplayName = "Dotnet Test",
                AppTypeSlug = "dotnet-app"
            },
            CancellationToken.None
        );

        await Should.NotThrowAsync
        (
            () => _writer.RenderAsync(app, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportFromDiskAsync_FlatJson_ReturnsEntriesAndEmptySkipped()
    {
        var app = await CreateStaticSiteAppAsync();

        var sourcePath = Path.Combine(_tempArtifactDir, "config.json");

        await File.WriteAllTextAsync
        (
            sourcePath,
            """{"apiBaseUrl":"https://api.example.com","theme":"dark"}""",
            CancellationToken.None
        );

        var result = await _writer.ImportFromDiskAsync(app, CancellationToken.None);

        result.Imported.Count.ShouldBe(2);
        result.Imported["apiBaseUrl"].ShouldBe("https://api.example.com");
        result.Imported["theme"].ShouldBe("dark");
        result.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public async Task ImportFromDiskAsync_MixedJson_SkipsNonFlatEntriesAndNamesThem()
    {
        // Bill ruling S55 #6: importer is flat-JSON only, surfaces a warning
        // naming the skipped non-flat entries (nested objects, arrays, nulls,
        // non-string primitives). The operator decides whether to manage the
        // skipped entries via the source file or convert them by hand.
        var app = await CreateStaticSiteAppAsync();

        var sourcePath = Path.Combine(_tempArtifactDir, "config.json");

        await File.WriteAllTextAsync
        (
            sourcePath,
            """
            {
              "apiBaseUrl": "https://api.example.com",
              "features": { "darkMode": true },
              "allowedOrigins": ["a", "b"],
              "maxRetries": 5,
              "nullable": null,
              "theme": "amber"
            }
            """,
            CancellationToken.None
        );

        var result = await _writer.ImportFromDiskAsync(app, CancellationToken.None);

        result.Imported.Count.ShouldBe(2);
        result.Imported["apiBaseUrl"].ShouldBe("https://api.example.com");
        result.Imported["theme"].ShouldBe("amber");

        result.Skipped.Count.ShouldBe(4);
        result.Skipped.ShouldContain("features");
        result.Skipped.ShouldContain("allowedOrigins");
        result.Skipped.ShouldContain("maxRetries");
        result.Skipped.ShouldContain("nullable");
    }

    [Fact]
    public async Task ImportFromDiskAsync_FileMissing_Throws()
    {
        var app = await CreateStaticSiteAppAsync();

        await Should.ThrowAsync<RuntimeConfigFileWriteException>
        (
            () => _writer.ImportFromDiskAsync(app, CancellationToken.None)
        );
    }

    [Fact]
    public async Task ImportFromDiskAsync_JsonArrayRoot_ThrowsActionable()
    {
        // F1 (Kai S55 #7): a config.json that parses to a JsonArray must surface
        // as a 400-with-actionable-message (RuntimeConfigFileWriteException), not
        // a 500 from an unwrapped InvalidOperationException via .AsObject().
        var app = await CreateStaticSiteAppAsync();

        var sourcePath = Path.Combine(_tempArtifactDir, "config.json");

        await File.WriteAllTextAsync(sourcePath, "[1, 2, 3]", CancellationToken.None);

        var exception = await Should.ThrowAsync<RuntimeConfigFileWriteException>
        (
            () => _writer.ImportFromDiskAsync(app, CancellationToken.None)
        );

        exception.Message.ShouldContain("is not a JSON object");
        exception.Message.ShouldContain(sourcePath);
    }

    [Fact]
    public async Task ImportFromDiskAsync_JsonScalarRoot_ThrowsActionable()
    {
        // F1 companion: a scalar root (string, number, bool) must funnel to the
        // same 400-class path as the JsonArray case above. Covers the second
        // shape JsonNode.Parse can return that is not a JsonObject.
        var app = await CreateStaticSiteAppAsync();

        var sourcePath = Path.Combine(_tempArtifactDir, "config.json");

        await File.WriteAllTextAsync(sourcePath, "\"just a string\"", CancellationToken.None);

        var exception = await Should.ThrowAsync<RuntimeConfigFileWriteException>
        (
            () => _writer.ImportFromDiskAsync(app, CancellationToken.None)
        );

        exception.Message.ShouldContain("is not a JSON object");
        exception.Message.ShouldContain(sourcePath);
    }
}

// In-memory SQLite factory for isolated testing -- each test gets a fresh
// context over the shared connection (which preserves the schema for the
// test lifetime). Mirrors the pattern in CapabilityStoreTests.
file sealed class InMemoryDbContextFactory
(
    SqliteConnection connection
) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return new AppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .ConfigureWarnings
            (
                warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;

        return Task.FromResult(new AppDbContext(options));
    }
}
