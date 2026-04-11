using System.Reflection;
using System.Threading.Channels;

using Collabhost.Api.Events;

namespace Collabhost.Api.Data.AppTypes;

public class TypeStore
(
    IEventBus<TypeStoreReloadedEvent> eventBus,
    TypeStoreSettings settings,
    ILogger<TypeStore> logger
) : IDisposable
{
    private readonly IEventBus<TypeStoreReloadedEvent> _eventBus = eventBus
        ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly TypeStoreSettings _settings = settings
        ?? throw new ArgumentNullException(nameof(settings));

    private readonly ILogger<TypeStore> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private volatile TypeStoreSnapshot _snapshot = new
    (
        [],
        FrozenDictionary<string, AppType>.Empty,
        FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty
    );

    // Built-in snapshot loaded from embedded resources, cached for reload
    private TypeStoreSnapshot _builtInSnapshot = new
    (
        [],
        FrozenDictionary<string, AppType>.Empty,
        FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty
    );

    // File watcher for user types directory
    private FileSystemWatcher? _fileWatcher;
    private readonly Channel<bool> _reloadChannel = Channel.CreateBounded<bool>(1);
    private Task? _reloadProcessorTask;
    private CancellationTokenSource? _shutdownCancellation;
    private bool _disposed;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var builtInSources = await ReadEmbeddedResourcesAsync(cancellationToken);

        var builtInErrors = TypeStoreValidator.Validate(builtInSources);

        if (builtInErrors.Count > 0)
        {
            foreach (var error in builtInErrors)
            {
                _logger.LogCritical
                (
                    "TypeStore validation error in {Source}: {FieldPath} -- {Message}",
                    error.Source,
                    error.FieldPath,
                    error.Message
                );
            }

            throw new TypeStoreValidationException(builtInErrors);
        }

        var builtInSnapshot = BuildSnapshot(builtInSources, true);

        _builtInSnapshot = builtInSnapshot;

        // Load user types from the scan directory
        var userTypesDirectory = ResolveUserTypesDirectory();
        var userSources = ReadUserTypesDirectory(userTypesDirectory);

        if (userSources.Count > 0)
        {
            var userErrors = TypeStoreValidator.ValidateUserTypes(userSources, _builtInSnapshot.Types);

            if (userErrors.Count > 0)
            {
                foreach (var error in userErrors)
                {
                    _logger.LogCritical
                    (
                        "TypeStore validation error in user type {Source}: {FieldPath} -- {Message}",
                        error.Source,
                        error.FieldPath,
                        error.Message
                    );
                }

                throw new TypeStoreValidationException(userErrors);
            }

            var userSnapshot = BuildSnapshot(userSources, false);

            var combinedSnapshot = CombineSnapshots(builtInSnapshot, userSnapshot);

            Interlocked.Exchange(ref _snapshot, combinedSnapshot);

            _logger.LogInformation
            (
                "TypeStore loaded: {BuiltInCount} built-in + {UserCount} user types, {BindingCount} bindings",
                _builtInSnapshot.Types.Count,
                userSnapshot.Types.Count,
                combinedSnapshot.BindingsByTypeSlug.Values
                    .Sum(bindings => bindings.Count)
            );
        }
        else
        {
            Interlocked.Exchange(ref _snapshot, builtInSnapshot);

            _logger.LogInformation
            (
                "TypeStore loaded: {TypeCount} built-in types, {BindingCount} bindings",
                builtInSnapshot.Types.Count,
                builtInSnapshot.BindingsByTypeSlug.Values
                    .Sum(bindings => bindings.Count)
            );
        }
    }

    public void StartWatching()
    {
        var userTypesDirectory = ResolveUserTypesDirectory();

        if (!Directory.Exists(userTypesDirectory))
        {
            _logger.LogInformation
            (
                "User types directory does not exist, skipping file watcher: {Directory}",
                userTypesDirectory
            );

            return;
        }

        _shutdownCancellation = new CancellationTokenSource();

        _reloadProcessorTask = ProcessReloadsAsync(_shutdownCancellation.Token);

        _fileWatcher = new FileSystemWatcher(userTypesDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _fileWatcher.Created += OnFileChanged;
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Deleted += OnFileChanged;
        _fileWatcher.Renamed += OnFileRenamed;

        _logger.LogInformation
        (
            "TypeStore file watcher started on: {Directory}",
            userTypesDirectory
        );
    }

    public async Task StopWatchingAsync()
    {
        if (_fileWatcher is not null)
        {
            _fileWatcher.EnableRaisingEvents = false;
            _fileWatcher.Dispose();
            _fileWatcher = null;
        }

        _reloadChannel.Writer.TryComplete();

        if (_reloadProcessorTask is not null && _shutdownCancellation is not null)
        {
            await _shutdownCancellation.CancelAsync();

            try
            {
                // Awaiting the background processor -- intentional shutdown coordination
#pragma warning disable VSTHRD003
                await _reloadProcessorTask;
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
    }

    public AppType? GetBySlug(string slug) =>
        _snapshot.TypesBySlug.GetValueOrDefault(slug);

    public IReadOnlyList<AppType> ListTypes() =>
        _snapshot.Types;

    public IReadOnlyDictionary<string, string>? GetBindings(string appTypeSlug) =>
        _snapshot.BindingsByTypeSlug.GetValueOrDefault(appTypeSlug);

    public bool HasBinding(string appTypeSlug, string capabilitySlug) =>
        _snapshot.BindingsByTypeSlug.TryGetValue(appTypeSlug, out var bindings)
        && bindings.ContainsKey(capabilitySlug);

    private void OnFileChanged(object sender, FileSystemEventArgs e) =>
        _reloadChannel.Writer.TryWrite(true);

    private void OnFileRenamed(object sender, RenamedEventArgs e) =>
        _reloadChannel.Writer.TryWrite(true);

    private async Task ProcessReloadsAsync(CancellationToken cancellationToken)
    {
        while (await _reloadChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            // Drain all pending signals -- values are irrelevant, channel is a notification mechanism
            while (_reloadChannel.Reader.TryRead(out _))
            {
                // Intentionally empty -- draining the channel
            }

            // Small delay to coalesce rapid FSW events
            await Task.Delay(500, cancellationToken);

            // Drain any additional signals that arrived during the delay
            while (_reloadChannel.Reader.TryRead(out _))
            {
                // Intentionally empty -- draining the channel
            }

            try
            {
                await ReloadAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TypeStore reload failed");
            }
        }
    }

    private Task ReloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userTypesDirectory = ResolveUserTypesDirectory();
        var userSources = ReadUserTypesDirectory(userTypesDirectory);

        if (userSources.Count == 0)
        {
            // No user type files -- use the cached built-in snapshot directly
            Interlocked.Exchange(ref _snapshot, _builtInSnapshot);

            var builtInBindingCount = _builtInSnapshot.BindingsByTypeSlug.Values
                .Sum(bindings => bindings.Count);

            _logger.LogInformation
            (
                "TypeStore reloaded: {BuiltInCount} built-in + 0 user types, {BindingCount} bindings",
                _builtInSnapshot.Types.Count,
                builtInBindingCount
            );

            _eventBus.Publish(new TypeStoreReloadedEvent
            (
                _builtInSnapshot.Types.Count,
                0,
                builtInBindingCount
            ));

            return Task.CompletedTask;
        }

        var errors = TypeStoreValidator.ValidateUserTypes(userSources, _builtInSnapshot.Types);

        if (errors.Count > 0)
        {
            _logger.LogWarning
            (
                "TypeStore reload rejected -- {ErrorCount} validation errors, preserving current snapshot",
                errors.Count
            );

            foreach (var error in errors)
            {
                _logger.LogWarning
                (
                    "TypeStore reload validation error in {Source}: {FieldPath} -- {Message}",
                    error.Source,
                    error.FieldPath,
                    error.Message
                );
            }

            return Task.CompletedTask;
        }

        var userSnapshot = BuildSnapshot(userSources, false);
        var combinedSnapshot = CombineSnapshots(_builtInSnapshot, userSnapshot);

        Interlocked.Exchange(ref _snapshot, combinedSnapshot);

        var bindingCount = combinedSnapshot.BindingsByTypeSlug.Values
            .Sum(bindings => bindings.Count);

        _logger.LogInformation
        (
            "TypeStore reloaded: {BuiltInCount} built-in + {UserCount} user types, {BindingCount} bindings",
            _builtInSnapshot.Types.Count,
            userSnapshot.Types.Count,
            bindingCount
        );

        _eventBus.Publish(new TypeStoreReloadedEvent
        (
            _builtInSnapshot.Types.Count,
            userSnapshot.Types.Count,
            bindingCount
        ));

        return Task.CompletedTask;
    }

    private string ResolveUserTypesDirectory()
    {
        var configuredPath = _settings.UserTypesDirectory;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    private static List<(string FileName, string Json)> ReadUserTypesDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var files = Directory.GetFiles(directoryPath, "*.json")
            .Order(StringComparer.Ordinal)
                .ToList();

        var sources = new List<(string FileName, string Json)>(files.Count);

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var json = File.ReadAllText(filePath);

            sources.Add((fileName, json));
        }

        return sources;
    }

    private static async Task<IReadOnlyList<(string ResourceName, string Json)>> ReadEmbeddedResourcesAsync
    (
        CancellationToken cancellationToken
    )
    {
        var assembly = Assembly.GetExecutingAssembly();

        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && name.Contains("BuiltInTypes", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
                .ToList();

        var sources = new List<(string ResourceName, string Json)>(resourceNames.Count);

        foreach (var resourceName in resourceNames)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

            using var reader = new StreamReader(stream);

            var json = await reader.ReadToEndAsync(cancellationToken);

            sources.Add((resourceName, json));
        }

        return sources;
    }

    private static TypeStoreSnapshot BuildSnapshot
    (
        IReadOnlyList<(string ResourceName, string Json)> sources,
        bool isBuiltIn
    )
    {
        var types = new List<AppType>(sources.Count);
        var typesBySlug = new Dictionary<string, AppType>(sources.Count, StringComparer.Ordinal);
        var bindingsByTypeSlug = new Dictionary<string, IReadOnlyDictionary<string, string>>(sources.Count, StringComparer.Ordinal);

        foreach (var (resourceName, json) in sources)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var slug = root.GetProperty("slug").GetString()
                ?? throw new InvalidOperationException($"Null slug in validated resource '{resourceName}'.");

            var displayName = root.GetProperty("displayName").GetString()
                ?? throw new InvalidOperationException($"Null displayName in validated resource '{resourceName}'.");

            var description = root.TryGetProperty("description", out var descriptionElement)
                && descriptionElement.ValueKind == JsonValueKind.String
                    ? descriptionElement.GetString()
                    : null;

            AppTypeMetadata? metadata = null;

            if (root.TryGetProperty("metadata", out var metadataElement)
                && metadataElement.ValueKind == JsonValueKind.Object)
            {
                metadata = JsonSerializer.Deserialize<AppTypeMetadata>(metadataElement.GetRawText(), _jsonOptions);
            }

            var typeDefinition = new AppType
            {
                Slug = slug,
                DisplayName = displayName,
                Description = description,
                Metadata = metadata,
                IsBuiltIn = isBuiltIn
            };

            types.Add(typeDefinition);
            typesBySlug[slug] = typeDefinition;

            // Build bindings dictionary for this type
            var bindings = new Dictionary<string, string>(StringComparer.Ordinal);

            if (root.TryGetProperty("bindings", out var bindingsElement)
                && bindingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bindingsElement.EnumerateObject())
                {
                    bindings[binding.Name] = binding.Value.GetRawText();
                }
            }

            bindingsByTypeSlug[slug] = bindings;
        }

        return new TypeStoreSnapshot
        (
            types.AsReadOnly(),
            typesBySlug.ToFrozenDictionary(StringComparer.Ordinal),
            bindingsByTypeSlug.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }

    private static TypeStoreSnapshot CombineSnapshots
    (
        TypeStoreSnapshot builtInSnapshot,
        TypeStoreSnapshot userSnapshot
    )
    {
        var allTypes = new List<AppType>(builtInSnapshot.Types.Count + userSnapshot.Types.Count);
        allTypes.AddRange(builtInSnapshot.Types);
        allTypes.AddRange(userSnapshot.Types);

        var allTypesBySlug = new Dictionary<string, AppType>(allTypes.Count, StringComparer.Ordinal);

        foreach (var type in allTypes)
        {
            allTypesBySlug[type.Slug] = type;
        }

        var allBindingsByTypeSlug = new Dictionary<string, IReadOnlyDictionary<string, string>>(allTypes.Count, StringComparer.Ordinal);

        foreach (var (slug, bindings) in builtInSnapshot.BindingsByTypeSlug)
        {
            allBindingsByTypeSlug[slug] = bindings;
        }

        foreach (var (slug, bindings) in userSnapshot.BindingsByTypeSlug)
        {
            allBindingsByTypeSlug[slug] = bindings;
        }

        return new TypeStoreSnapshot
        (
            allTypes.AsReadOnly(),
            allTypesBySlug.ToFrozenDictionary(StringComparer.Ordinal),
            allBindingsByTypeSlug.ToFrozenDictionary(StringComparer.Ordinal)
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _fileWatcher?.Dispose();
            _shutdownCancellation?.Dispose();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
