using System.Globalization;

using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;

using Microsoft.Extensions.Logging.Abstractions;

using Shouldly;

using Xunit;

namespace Collabhost.Api.Tests.Data;

public class TypeStoreUserTypeTests : IDisposable
{
    private readonly string _userTypesDirectory;

    private static readonly string _validUserTypeJson = """
        {
          "slug": "custom-app",
          "displayName": "Custom Application",
          "description": "A custom user type",
          "bindings": {
            "process": {
              "discoveryStrategy": "Manual",
              "shutdownTimeoutSeconds": 10
            }
          }
        }
        """;

    public TypeStoreUserTypeTests()
    {
        _userTypesDirectory = Path.Combine
        (
            Path.GetTempPath(),
            "collabhost-user-types-test-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
        );

        Directory.CreateDirectory(_userTypesDirectory);
    }

    private TypeStore CreateTypeStore() =>
        new
        (
            new EventBus<TypeStoreReloadedEvent>(),
            new TypeStoreSettings { UserTypesDirectory = _userTypesDirectory },
            NullLogger<TypeStore>.Instance
        );

    private TypeStore CreateTypeStoreWithEventBus(EventBus<TypeStoreReloadedEvent> eventBus) =>
        new
        (
            eventBus,
            new TypeStoreSettings { UserTypesDirectory = _userTypesDirectory },
            NullLogger<TypeStore>.Instance
        );

    [Fact]
    public async Task LoadAsync_WithUserType_LoadsBothBuiltInAndUserTypes()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(6);

        var customType = store.GetBySlug("custom-app");
        customType.ShouldNotBeNull();
        customType.IsBuiltIn.ShouldBeFalse();
        customType.DisplayName.ShouldBe("Custom Application");
    }

    [Fact]
    public async Task LoadAsync_WithUserType_BuiltInTypesStillPresent()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        var dotnetApp = store.GetBySlug("dotnet-app");
        dotnetApp.ShouldNotBeNull();
        dotnetApp.IsBuiltIn.ShouldBeTrue();
    }

    [Fact]
    public async Task LoadAsync_WithUserType_UserTypeBindingsAvailable()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        var bindings = store.GetBindings("custom-app");
        bindings.ShouldNotBeNull();
        bindings.ShouldContainKey("process");
    }

    [Fact]
    public async Task LoadAsync_NoUserTypesDirectory_LoadsOnlyBuiltInTypes()
    {
        // Delete the user types directory so it doesn't exist
        Directory.Delete(_userTypesDirectory, recursive: true);

        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(5);
        store.ListTypes().ShouldAllBe(type => type.IsBuiltIn);
    }

    [Fact]
    public async Task LoadAsync_EmptyUserTypesDirectory_LoadsOnlyBuiltInTypes()
    {
        // Directory exists but has no JSON files
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(5);
    }

    [Fact]
    public async Task LoadAsync_UserTypeSlugConflictsWithBuiltIn_Throws()
    {
        var conflictingJson = """
            {
              "slug": "dotnet-app",
              "displayName": "My Conflicting Type"
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "dotnet-app.json"), conflictingJson);

        var store = CreateTypeStore();

        var exception = await Should.ThrowAsync<TypeStoreValidationException>(store.LoadAsync());

        exception.Errors.ShouldContain(error =>
            error.FieldPath == "slug"
            && error.Message.Contains("conflicts with a built-in type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_UserTypeDisplayNameConflictsWithBuiltIn_Throws()
    {
        var conflictingJson = """
            {
              "slug": "unique-slug",
              "displayName": ".NET Application"
            }
            """;

        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "unique-slug.json"), conflictingJson);

        var store = CreateTypeStore();

        var exception = await Should.ThrowAsync<TypeStoreValidationException>(store.LoadAsync());

        exception.Errors.ShouldContain(error =>
            error.FieldPath == "displayName"
            && error.Message.Contains("conflicts with a built-in type", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FileWatch_NewFile_TriggersReloadAndAddsType()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(5);

        store.StartWatching();

        try
        {
            // Write a new user type file
            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

            // Wait for the FileSystemWatcher + debounce (500ms) + processing time
            await WaitForConditionAsync
            (
                () => store.ListTypes().Count == 6,
                timeoutMilliseconds: 5000
            );

            store.ListTypes().Count.ShouldBe(6);

            var customType = store.GetBySlug("custom-app");
            customType.ShouldNotBeNull();
            customType.IsBuiltIn.ShouldBeFalse();
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_InvalidFile_PreservesOldSnapshot()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(6);

        store.StartWatching();

        try
        {
            // Overwrite with invalid JSON
            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), "{ broken json");

            // Wait for the reload attempt to process
            await Task.Delay(2000);

            // Old snapshot should be preserved
            store.ListTypes().Count.ShouldBe(6);
            store.GetBySlug("custom-app").ShouldNotBeNull();
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_DeleteFile_TriggersReloadAndRemovesType()
    {
        var filePath = Path.Combine(_userTypesDirectory, "custom-app.json");

        await File.WriteAllTextAsync(filePath, _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(6);

        store.StartWatching();

        try
        {
            File.Delete(filePath);

            await WaitForConditionAsync
            (
                () => store.ListTypes().Count == 5,
                timeoutMilliseconds: 5000
            );

            store.ListTypes().Count.ShouldBe(5);
            store.GetBySlug("custom-app").ShouldBeNull();
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_BuiltInTypesUnaffectedByReload()
    {
        var store = CreateTypeStore();

        await store.LoadAsync();

        var dotnetTypeBefore = store.GetBySlug("dotnet-app");
        dotnetTypeBefore.ShouldNotBeNull();

        store.StartWatching();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

            await WaitForConditionAsync
            (
                () => store.ListTypes().Count == 6,
                timeoutMilliseconds: 5000
            );

            // Built-in type should still be intact
            var dotnetTypeAfter = store.GetBySlug("dotnet-app");
            dotnetTypeAfter.ShouldNotBeNull();
            dotnetTypeAfter.IsBuiltIn.ShouldBeTrue();
            dotnetTypeAfter.DisplayName.ShouldBe(".NET Application");
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_SuccessfulReload_PublishesEvent()
    {
        var eventBus = new EventBus<TypeStoreReloadedEvent>();
        TypeStoreReloadedEvent? receivedEvent = null;
        eventBus.Subscribe(evt => receivedEvent = evt);

        var store = CreateTypeStoreWithEventBus(eventBus);

        await store.LoadAsync();

        store.StartWatching();

        try
        {
            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

            await WaitForConditionAsync
            (
                () => receivedEvent is not null,
                timeoutMilliseconds: 5000
            );

            receivedEvent.ShouldNotBeNull();
            receivedEvent.BuiltInCount.ShouldBe(5);
            receivedEvent.UserCount.ShouldBe(1);
            receivedEvent.BindingCount.ShouldBeGreaterThan(0);
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_FailedReload_DoesNotPublishEvent()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var eventBus = new EventBus<TypeStoreReloadedEvent>();
        var eventCount = 0;
        eventBus.Subscribe(_ => Interlocked.Increment(ref eventCount));

        var store = CreateTypeStoreWithEventBus(eventBus);

        await store.LoadAsync();

        store.StartWatching();

        try
        {
            // Overwrite with invalid JSON to trigger a failed reload
            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), "not json");

            await Task.Delay(2000);

            // No reload event should have been published
            eventCount.ShouldBe(0);
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_SlugConflictOnReload_PreservesOldSnapshot()
    {
        await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "custom-app.json"), _validUserTypeJson);

        var store = CreateTypeStore();

        await store.LoadAsync();

        store.ListTypes().Count.ShouldBe(6);

        store.StartWatching();

        try
        {
            // Add a file that conflicts with a built-in slug
            var conflictingJson = """
                {
                  "slug": "dotnet-app",
                  "displayName": "My Conflicting Type"
                }
                """;

            await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, "dotnet-app.json"), conflictingJson);

            await Task.Delay(2000);

            // Snapshot should be preserved with the 6 types (5 built-in + 1 valid user)
            store.ListTypes().Count.ShouldBe(6);
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    [Fact]
    public async Task FileWatch_RapidWrites_CoalescesToSingleReload()
    {
        var eventBus = new EventBus<TypeStoreReloadedEvent>();
        var reloadCount = 0;
        eventBus.Subscribe(_ => Interlocked.Increment(ref reloadCount));

        var store = CreateTypeStoreWithEventBus(eventBus);

        await store.LoadAsync();

        store.StartWatching();

        try
        {
            // Write 5 files in rapid succession
            for (var i = 1; i <= 5; i++)
            {
                var slug = string.Create(CultureInfo.InvariantCulture, $"rapid-app-{i}");
                var displayName = string.Create(CultureInfo.InvariantCulture, $"Rapid App {i}");

                var json = $$"""
                    {
                      "slug": "{{slug}}",
                      "displayName": "{{displayName}}"
                    }
                    """;

                await File.WriteAllTextAsync(Path.Combine(_userTypesDirectory, $"{slug}.json"), json);
            }

            // Wait long enough for coalescing to happen
            await Task.Delay(3000);

            // Should have coalesced into at most a few reloads (certainly fewer than 5)
            reloadCount.ShouldBeGreaterThan(0);
            reloadCount.ShouldBeLessThanOrEqualTo(3);

            // All 5 user types should be loaded
            store.ListTypes().Count.ShouldBe(10);
        }
        finally
        {
            await store.StopWatchingAsync();
        }
    }

    private static async Task WaitForConditionAsync
    (
        Func<bool> condition,
        int timeoutMilliseconds = 5000,
        int pollIntervalMilliseconds = 100
    )
    {
        var elapsed = 0;

        while (!condition() && elapsed < timeoutMilliseconds)
        {
            await Task.Delay(pollIntervalMilliseconds);
            elapsed += pollIntervalMilliseconds;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_userTypesDirectory))
        {
            try
            {
                Directory.Delete(_userTypesDirectory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        GC.SuppressFinalize(this);
    }
}
