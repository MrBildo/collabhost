using System.Collections.Concurrent;
using System.Globalization;

using Collabhost.Api.ActivityLog;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Capabilities.Configurations;
using Collabhost.Api.Data.AppTypes;
using Collabhost.Api.Events;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;
using Collabhost.Api.Supervisor.Containment;

namespace Collabhost.Api.Supervisor;

public class ProcessSupervisor
(
    IManagedProcessRunner runner,
    IProcessContainment containment,
    AppStore appStore,
    CapabilityStore capabilityStore,
    TypeStore typeStore,
    IEventBus<ProcessStateChangedEvent> eventBus,
    IEnumerable<IProcessArgumentProvider> argumentProviders,
    IEnumerable<IProcessEnvironmentProvider> environmentProviders,
    IEnumerable<IReservedPortInitializer> reservedPortInitializers,
    HostedAppBundleDirectory hostedAppBundleDirectory,
    PortAllocator portAllocator,
    ActivityEventStore activityEventStore,
    ILogger<ProcessSupervisor> logger
) : IHostedService, IDisposable
{
    private readonly IManagedProcessRunner _runner = runner
        ?? throw new ArgumentNullException(nameof(runner));

    private readonly IProcessContainment _containment = containment
        ?? throw new ArgumentNullException(nameof(containment));

    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly CapabilityStore _capabilityStore = capabilityStore
        ?? throw new ArgumentNullException(nameof(capabilityStore));

    private readonly TypeStore _typeStore = typeStore
        ?? throw new ArgumentNullException(nameof(typeStore));

    private readonly IEventBus<ProcessStateChangedEvent> _eventBus = eventBus
        ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly IProcessArgumentProvider[] _argumentProviders =
        [.. (argumentProviders ?? throw new ArgumentNullException(nameof(argumentProviders)))];

    private readonly IProcessEnvironmentProvider[] _environmentProviders =
        [.. (environmentProviders ?? throw new ArgumentNullException(nameof(environmentProviders)))];

    private readonly IReservedPortInitializer[] _reservedPortInitializers =
        [.. (reservedPortInitializers ?? throw new ArgumentNullException(nameof(reservedPortInitializers)))];

    private readonly HostedAppBundleDirectory _hostedAppBundleDirectory = hostedAppBundleDirectory
        ?? throw new ArgumentNullException(nameof(hostedAppBundleDirectory));

    private readonly PortAllocator _portAllocator = portAllocator
        ?? throw new ArgumentNullException(nameof(portAllocator));

    private readonly ActivityEventStore _activityEventStore = activityEventStore
        ?? throw new ArgumentNullException(nameof(activityEventStore));

    private readonly ILogger<ProcessSupervisor> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<Ulid, ManagedProcess> _processes = new();
    private readonly ConcurrentDictionary<Ulid, RestartPolicy> _restartPolicies = new();
    private readonly ProcessLogBufferStore _logBufferStore = new();
    private Timer? _graceTimer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor starting -- checking for auto-start apps");

        _graceTimer = new Timer(CheckGracePeriods, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        try
        {
            var apps = await _appStore.ListAsync(cancellationToken);

            // Reserve every pinned port before any automatic allocation runs.
            // A pinned app that is stopped (or not set to auto-start) still owns
            // its port -- otherwise an automatic allocation for a different app
            // could be handed that number while the pinned app is down, and the
            // pinned app would fail to bind when it next starts.
            await HydratePinnedPortReservationsAsync(apps, cancellationToken);

            // Infrastructure consumers (the proxy admin port) claim their ports now
            // -- after every pin is reserved, before anything auto-starts -- so their
            // allocation excludes pinned ports and cannot be handed a number a pin
            // owns. The proxy process may itself be auto-started in the loop below,
            // and it reads its admin port at start; the port must be set before that.
            foreach (var initializer in _reservedPortInitializers)
            {
                initializer.Initialize(_portAllocator);
            }

            foreach (var app in apps)
            {
                var autoStartConfiguration = await _capabilityStore.ResolveAsync<AutoStartConfiguration>
                (
                    "auto-start", app, cancellationToken
                );

                if (autoStartConfiguration is null || !autoStartConfiguration.Enabled)
                {
                    continue;
                }

                // Operator-stopped state survives Collabhost restart (card #350). If the
                // operator stopped this app via the Stop / Kill endpoint before the
                // restart, the persisted StoppedByOperator flag tells the supervisor to
                // skip auto-start. The flag clears on the next Start operation. The
                // peer hydration for routing-only apps lives in
                // ProxyManager.HydrateRouteStatesFromPersistenceAsync.
                if (app.StoppedByOperator)
                {
                    _logger.LogInformation
                    (
                        "Skipping auto-start for '{DisplayName}' -- stopped by operator before restart",
                        app.DisplayName
                    );
                    continue;
                }

                var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");

                if (!hasProcess)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Auto-starting app '{DisplayName}'", app.DisplayName);

                    await StartAppInternalAsync(app.Id, cancellationToken);

                    try
                    {
                        await _activityEventStore.RecordAsync
                        (
                            new ActivityEvent
                            {
                                EventType = ActivityEventTypes.AppAutoStarted,
                                ActorId = ActivityActor.SystemId,
                                ActorName = ActivityActor.SystemName,
                                AppId = app.Id.ToString(null, CultureInfo.InvariantCulture),
                                AppSlug = app.Slug
                            },
                            CancellationToken.None
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", app.DisplayName);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError
                    (
                        exception,
                        "Failed to auto-start app '{DisplayName}'",
                        app.DisplayName
                    );
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to run auto-start check");
        }

        _logger.LogInformation("Process supervisor started");
    }

    private async Task HydratePinnedPortReservationsAsync
    (
        IReadOnlyCollection<App> apps,
        CancellationToken ct
    )
    {
        foreach (var app in apps)
        {
            var portInjectionConfiguration = await _capabilityStore.ResolveAsync<PortInjectionConfiguration>
            (
                "port-injection", app, ct
            );

            if (portInjectionConfiguration is { FixedPort: > 0 } pinned)
            {
                _portAllocator.Reserve(app.Id, pinned.FixedPort);

                _logger.LogInformation
                (
                    "Reserved fixed port {Port} for '{DisplayName}'",
                    pinned.FixedPort,
                    app.DisplayName
                );
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor stopping -- stopping all managed processes");

        if (_graceTimer is not null)
        {
            await _graceTimer.DisposeAsync();
        }

        // Cancel any pending restart-delay tasks BEFORE iterating processes.
        // Without this, a process in Backoff/Restarting state during shutdown will not
        // hit the IsRunning branch below; its Task.Delay completes after ApplicationStopping
        // fires and StartAppInternalAsync runs against a torn-down dictionary state. (#191)
        foreach (var process in _processes.Values)
        {
            process.CancelPendingRestart();
        }

        var stopTasks = _processes.Select
        (
            async kvp =>
            {
                var (appId, process) = kvp;

                // The lock-acquire here must not propagate the host's shutdown CT. The host's
                // linked shutdown token may already be signalled (host shutdown timeout, a second
                // SIGTERM, an internal sibling-service cancellation) by the time this closure runs.
                // SemaphoreSlim.WaitAsync(ct) with a signalled token throws TaskCanceledException
                // synchronously; aggregated through Task.WhenAll it escapes StopAsync as an
                // unhandled exception, and systemd marks the unit `failed (result: core-dump)`
                // instead of `inactive` -- even though the graceful shutdown work below has
                // already completed cleanly. The lock is uncontended at this point (the API has
                // stopped accepting requests; restart-retry closures hold no lock; pending
                // restart-delay tasks were cancelled in the loop above), so an unconditional
                // acquire is safe. The host CT is still honoured on the actual stop work
                // (StopProcessWithShutdownPolicyAsync below) -- propagation belongs ON the stop
                // work, not on the bookkeeping lock-acquire. Mirrors the grace-period site
                // below at the AcquireOperationLockAsync(CancellationToken.None) call. (#358)
                await using var _ = await process.AcquireOperationLockAsync(CancellationToken.None);

                if (process.IsRunning)
                {
                    await StopProcessWithShutdownPolicyAsync(appId, process, cancellationToken);

                    _logger.LogInformation
                    (
                        "Stopped app '{DisplayName}' (PID {Pid})",
                        process.DisplayName,
                        process.Pid
                    );
                }

                process.Dispose();
            }
        );

        await Task.WhenAll(stopTasks);

        _processes.Clear();
        _restartPolicies.Clear();
        _logBufferStore.Clear();

        _logger.LogInformation("Process supervisor stopped");
    }

    public async Task<ManagedProcess> StartAppAsync(Ulid appId, CancellationToken ct = default)
    {
        // If a previous ManagedProcess exists (e.g. after stop), dispose it BEFORE
        // starting the new one. We must not hold the old process's operation lock
        // across StartAppInternalAsync because that method removes and disposes the
        // old process, which invalidates the semaphore -- causing the lock's
        // DisposeAsync to throw ObjectDisposedException on Release().
        if (_processes.TryGetValue(appId, out var existing) && !existing.IsRunning)
        {
            _processes.TryRemove(appId, out _);
            existing.Dispose();
        }

        return await StartAppInternalAsync(appId, ct);
    }

    public async Task<ManagedProcess> StopAppAsync(Ulid appId, CancellationToken ct = default)
    {
        if (!_processes.TryGetValue(appId, out var process) || process.IsStopped)
        {
            throw new InvalidOperationException("App is already stopped.");
        }

        await using var _ = await process.AcquireOperationLockAsync(ct);

        process.MarkStoppedByOperator();

        // Persist the operator-stop intent. Mirrors the in-memory MarkStoppedByOperator()
        // call onto persistence so the state survives Collabhost restart. Best-effort --
        // a DB failure here does not abort the stop; the in-memory flag still suppresses
        // crash-restart within this lifetime, and the operator can re-stop after restart
        // to converge. Card #350.
        try
        {
            await _appStore.SetStoppedByOperatorAsync(appId, process.AppSlug, true, ct);
        }
        catch (Exception persistException)
        {
            _logger.LogWarning
            (
                persistException,
                "Failed to persist StoppedByOperator flag for '{DisplayName}'",
                process.DisplayName
            );
        }

        await StopProcessWithShutdownPolicyAsync(appId, process, ct);

        _logger.LogInformation("Stopped app '{DisplayName}'", process.DisplayName);

        return process;
    }

    public async Task<ManagedProcess> RestartAppAsync(Ulid appId, CancellationToken ct = default)
    {
        ManagedProcess? stopped = null;

        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            await using var operationLock = await existing.AcquireOperationLockAsync(ct);

            existing.MarkStoppedByOperator();

            await StopProcessWithShutdownPolicyAsync(appId, existing, ct);

            stopped = existing;
        }

        // Dispose the old process after releasing the operation lock to avoid
        // ObjectDisposedException -- the lock's DisposeAsync calls Release() on
        // the semaphore, which fails if the process (and its semaphore) is already disposed
        if (stopped is not null)
        {
            _processes.TryRemove(appId, out _);
            stopped.Dispose();
        }

        return await StartAppInternalAsync(appId, ct);
    }

    public async Task KillAppAsync(Ulid appId, CancellationToken ct = default)
    {
        if (!_processes.TryGetValue(appId, out var process))
        {
            throw new InvalidOperationException("No managed process found for this app.");
        }

        await using var _ = await process.AcquireOperationLockAsync(ct);

        process.MarkStoppedByOperator();

        // Persist the operator-stop intent (kill is operator-driven). Card #350.
        try
        {
            await _appStore.SetStoppedByOperatorAsync(appId, process.AppSlug, true, ct);
        }
        catch (Exception persistException)
        {
            _logger.LogWarning
            (
                persistException,
                "Failed to persist StoppedByOperator flag for '{DisplayName}' on kill",
                process.DisplayName
            );
        }

        process.KillProcess();

        var previous = process.MarkStopped();

        PublishStateChanged(process, previous);

        _logger.LogInformation("Killed app '{DisplayName}'", process.DisplayName);
    }

    public ManagedProcess? GetProcess(Ulid appId)
    {
        _processes.TryGetValue(appId, out var process);
        return process;
    }

    // Snapshot of all currently-tracked processes. The returned collection is a copy --
    // callers that iterate while a process starts, stops, or restarts will not see
    // their iteration corrupted, but they may see a stale entry for a single tick.
    public IReadOnlyCollection<ManagedProcess> GetProcesses() => [.. _processes.Values];

    public RingBuffer<LogEntry> GetOrCreateLogBuffer(Ulid appId) =>
        _logBufferStore.GetOrCreate(appId);

    public void CleanupDeletedApp(Ulid appId, string appSlug)
    {
        _logBufferStore.Remove(appId);
        _restartPolicies.TryRemove(appId, out _);

        // A deleted app's pinned-port reservation (if any) returns to the
        // automatic-allocation pool. No-op when the app was never pinned.
        _portAllocator.Release(appId);

        if (_processes.TryRemove(appId, out var process))
        {
            process.Dispose();
        }

        // Best-effort reap of the per-app single-file bundle-extraction dir
        // provisioned at start for hosted dotnet-apps. Slug-keyed and isolated,
        // so a non-dotnet app simply has no dir to remove. (#313.)
        _hostedAppBundleDirectory.Reap(appSlug);
    }

    private async Task<ManagedProcess> StartAppInternalAsync(Ulid appId, CancellationToken ct)
    {
        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            throw new InvalidOperationException("App is already running.");
        }

        var app = await _appStore.GetByIdAsync(appId, ct)
            ?? throw new InvalidOperationException("App not found.");

        var hasProcess = _typeStore.HasBinding(app.AppTypeSlug, "process");

        if (!hasProcess)
        {
            throw new InvalidOperationException("This app type does not have a process capability.");
        }

        var processConfiguration = await _capabilityStore.ResolveAsync<ProcessConfiguration>
        (
            "process", app, ct
        ) ?? throw new InvalidOperationException("Process capability configuration could not be resolved.");

        var artifactConfiguration = await _capabilityStore.ResolveAsync<ArtifactConfiguration>
        (
            "artifact", app, ct
        );

        if (artifactConfiguration is null || string.IsNullOrWhiteSpace(artifactConfiguration.Location))
        {
            ParkErrorProcess(appId, app, "Cannot start app: artifact location is not configured.");
            throw new InvalidOperationException("Cannot start app: artifact location is not configured. Set the artifact capability's location field.");
        }

        if (!Directory.Exists(artifactConfiguration.Location))
        {
            var errorMessage = $"Cannot start app: artifact location '{artifactConfiguration.Location}' does not exist on disk.";
            ParkErrorProcess(appId, app, errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        var effectiveWorkingDirectory = !string.IsNullOrWhiteSpace(processConfiguration.WorkingDirectory)
            ? Path.Combine(artifactConfiguration.Location, processConfiguration.WorkingDirectory)
            : artifactConfiguration.Location;

        var discoveredProcess = DiscoveryStrategyExecutor.Discover(processConfiguration, effectiveWorkingDirectory);

        // Allow subsystems to augment process arguments at start time.
        // This is how the proxy subsystem injects the dynamic admin port into Caddy's arguments.
        var augmentedArguments = discoveredProcess.Arguments;

        foreach (var provider in _argumentProviders)
        {
            augmentedArguments = provider.AugmentArguments(app.Slug, augmentedArguments);
        }

        if (!string.Equals(augmentedArguments, discoveredProcess.Arguments, StringComparison.Ordinal))
        {
            discoveredProcess = discoveredProcess with { Arguments = augmentedArguments };
        }

        var environmentConfiguration = await _capabilityStore.ResolveAsync<EnvironmentConfiguration>
        (
            "environment-defaults", app, ct
        );

        var operatorOverrideKeys = await GetEnvironmentOverrideKeysAsync(app.Id, ct);

        var capabilityVariables = environmentConfiguration?.Variables;

        // Provision a sandbox-writable single-file bundle-extraction dir for
        // hosted dotnet-apps that actually self-extract. Injected into the
        // capability-variables tier (not as an IProcessEnvironmentProvider) and
        // gated on the operator not having pinned the variable -- so an
        // explicit operator override wins silently, while the platform default
        // is always present otherwise. The self-extraction discriminator keeps
        // the dir from being fabricated for non-single-file publishes that do
        // no extraction at all. (#313 / #322 decision 3.)
        var isDotnetApp = string.Equals
        (
            app.AppTypeSlug,
            HostedDotnetBundleEnvironment.DotnetAppTypeSlug,
            StringComparison.Ordinal
        );

        var artifactSelfExtracts = isDotnetApp
            && HostedDotnetBundleEnvironment.ArtifactSelfExtracts(artifactConfiguration.Location);

        if (HostedDotnetBundleEnvironment.ShouldProvision(app.AppTypeSlug, operatorOverrideKeys, artifactSelfExtracts, out _))
        {
            var bundleDirectory = _hostedAppBundleDirectory.EnsureFor(app.Slug);

            capabilityVariables ??= new Dictionary<string, string>(StringComparer.Ordinal);
            capabilityVariables[HostedDotnetBundleEnvironment.BundleExtractBaseDirVariable] = bundleDirectory;
        }

        var environmentVariables = MergeEnvironmentVariables
        (
            capabilityVariables,
            operatorOverrideKeys,
            _environmentProviders,
            app.Slug,
            _logger
        );

        var managed = new ManagedProcess(app.Id, app.Slug, app.DisplayName);

        var portInjectionConfiguration = await _capabilityStore.ResolveAsync<PortInjectionConfiguration>
        (
            "port-injection", app, ct
        );

        if (portInjectionConfiguration is not null)
        {
            int effectivePort;

            // A non-zero FixedPort pins the app to a stable address so consumers
            // that reach it at localhost:<port> survive its restarts without
            // re-pointing. Reserve it so automatic allocation for any other app
            // can never be handed the same number. Zero (the default) keeps the
            // historical behavior: the platform picks a free port automatically.
            if (portInjectionConfiguration.FixedPort > 0)
            {
                effectivePort = portInjectionConfiguration.FixedPort;
                _portAllocator.Reserve(appId, effectivePort);

                // A reservation only keeps Collabhost from internally reassigning
                // the port -- it says nothing about an owner OUTSIDE Collabhost
                // (another host service, a container, a leftover process). The
                // pinned app is about to be told to bind this exact number; if it
                // is already taken the child would fail to bind and crash-loop
                // opaquely. Validate live availability here, at the point of bind,
                // and hard-fail attributably instead of letting it loop.
                // "Attributable" means the port was held at probe time: the probe
                // releases the port before the child binds it, so a port stolen in
                // the sub-millisecond window between probe and child-bind is an
                // inherent TOCTOU that falls to the normal crash path, not this
                // Fatal hard-stop. The probe still deterministically catches the
                // common case -- a port already held by another owner at start.
                if (!PortAllocator.IsPortAvailable(effectivePort))
                {
                    var reason = string.Format
                    (
                        CultureInfo.InvariantCulture,
                        "Cannot start app: pinned port {0} is already in use by another "
                        + "process on this machine. Free the port or change the pinned "
                        + "port in the app's settings, then start the app again.",
                        effectivePort
                    );

                    ParkErrorProcess(appId, app, reason, ProcessState.Fatal);

                    throw new InvalidOperationException(reason);
                }
            }
            else
            {
                // No pin (or an unpin: FixedPort went N->0). Release any prior
                // reservation this app held so unpinning returns the port to the
                // automatic pool in the same session, symmetric with delete --
                // not only on the next reboot. Release is a no-op when nothing
                // was reserved, so the unpinned-from-the-start case is unaffected.
                _portAllocator.Release(appId);

                effectivePort = await _portAllocator.AllocateAsync(ct);
            }

            managed.AssignPort(effectivePort);

            var formattedPortValue = portInjectionConfiguration.PortFormat
                .Replace
                (
                    "{port}",
                    effectivePort.ToString(CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase
                );

            // Port injection wins on name conflict with environment defaults
            environmentVariables[portInjectionConfiguration.EnvironmentVariableName] = formattedPortValue;
        }

        var previousState = managed.MarkStarting();
        managed.ClearStoppedByOperator();

        PublishStateChanged(managed, previousState);

        var startConfiguration = new ProcessStartConfiguration
        (
            discoveredProcess.Command,
            discoveredProcess.Arguments,
            discoveredProcess.WorkingDirectory,
            environmentVariables,
            (line, stream) => GetOrCreateLogBuffer(app.Id).Add(new LogEntry(DateTime.UtcNow, stream, line, LogLevelParser.ParseLevel(line)))
        );

        var handle = _runner.Start(startConfiguration);

        var container = _containment.CreateContainer(app.Slug);

        if (container is not null)
        {
            if (!container.AssignProcess(handle.Pid))
            {
                _logger.LogWarning("Failed to assign process to containment for '{Slug}'", app.Slug);
            }
        }

        managed.SetContainmentHandle(container);

        // Store the handle and PID without transitioning to Running --
        // the grace period task handles the Starting -> Running transition
        managed.SetHandle(handle);

        var restartConfiguration = await _capabilityStore.ResolveAsync<RestartConfiguration>
        (
            "restart", app, ct
        );

        _restartPolicies[appId] = restartConfiguration?.Policy ?? RestartPolicy.Never;

        // Wire exit handler BEFORE registering the process -- so we catch exits during startup
        handle.Exited += exitCode => OnProcessExited(appId, exitCode);

        _processes.TryRemove(appId, out var old);
        old?.Dispose();

        _processes[appId] = managed;

        // Clear the persisted operator-stop flag once the start has committed.
        // Mirrors the in-memory ClearStoppedByOperator() above onto persistence so
        // the state survives Collabhost restart. Best-effort -- a transient DB
        // failure here does not roll back the start (the process IS running), but
        // a subsequent restart could then skip auto-start; the operator can
        // re-start manually to converge. Card #350.
        try
        {
            await _appStore.SetStoppedByOperatorAsync(appId, app.Slug, false, ct);
        }
        catch (Exception persistException)
        {
            _logger.LogWarning
            (
                persistException,
                "Failed to clear persisted StoppedByOperator flag for '{DisplayName}'",
                app.DisplayName
            );
        }

        _logger.LogInformation
        (
            "Started app '{DisplayName}' (PID {Pid}, Port {Port})",
            app.DisplayName,
            handle.Pid,
            managed.Port?.ToString(CultureInfo.InvariantCulture) ?? "none"
        );

        // Grace period: wait before promoting Starting -> Running.
        // If the process exits during this window, OnProcessExited handles it as a startup failure.
        var gracePeriodSeconds = processConfiguration.StartupGracePeriodSeconds;

        // Grace period is intentionally fire-and-forget -- the task is self-contained
        // with full error handling and acquires the per-process lock before mutating state.
#pragma warning disable VSTHRD110, MA0134, CS4014, CA2016, MA0040
        Task.Run
        (
            async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(gracePeriodSeconds), CancellationToken.None);

                    // The ManagedProcess may have been disposed by a startup retry or operator stop
                    // between the delay and now. If so, this grace period task is stale -- bail out.
                    if (!_processes.TryGetValue(appId, out var current) || !ReferenceEquals(current, managed))
                    {
                        return;
                    }

                    await using var _ = await managed.AcquireOperationLockAsync(CancellationToken.None);

                    if (!managed.HasProcessExited && managed.State == ProcessState.Starting)
                    {
                        var previous = managed.MarkRunning();

                        PublishStateChanged(managed, previous);

                        _logger.LogInformation
                        (
                            "App '{DisplayName}' passed startup grace period ({Seconds}s)",
                            managed.DisplayName,
                            gracePeriodSeconds
                        );
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The ManagedProcess was disposed by a retry or restart -- this grace period
                    // task is stale and should silently exit
                }
                catch (Exception exception)
                {
                    _logger.LogError
                    (
                        exception,
                        "Error during startup grace period for '{DisplayName}'",
                        managed.DisplayName
                    );
                }
            }
        );
#pragma warning restore VSTHRD110, MA0134, CS4014, CA2016, MA0040

        return managed;
    }

    private void OnProcessExited(Ulid appId, int exitCode)
    {
        if (!_processes.TryGetValue(appId, out var process))
        {
            return;
        }

        if (process.State is ProcessState.Stopping or ProcessState.Stopped or ProcessState.Fatal)
        {
            return;
        }

        if (process.StoppedByOperator)
        {
            var previous = process.MarkCrashed(exitCode);

            PublishStateChanged(process, previous);

            _logger.LogInformation
            (
                "App '{DisplayName}' was stopped by operator -- skipping restart (exit code: {ExitCode})",
                process.DisplayName,
                exitCode
            );

            return;
        }

        if (process.State == ProcessState.Starting)
        {
            OnStartupFailure(appId, process, exitCode);
        }
        else
        {
            OnRuntimeCrash(appId, process, exitCode);
        }
    }

    // Called from synchronous Exited event callback -- sync-over-async is intentional
#pragma warning disable VSTHRD002
    private void OnStartupFailure(Ulid appId, ManagedProcess process, int exitCode)
    {
        var previousState = process.MarkBackoff(exitCode);

        PublishStateChanged(process, previousState);

        _logger.LogWarning
        (
            "App '{DisplayName}' failed to start (exit code: {ExitCode}, attempt: {Attempt})",
            process.DisplayName,
            exitCode,
            process.StartupFailures
        );

        // Resolve max startup retries from config -- use default if unavailable
        var maxStartupRetries = 3;

        try
        {
            var app = _appStore.GetByIdAsync(appId, CancellationToken.None).GetAwaiter().GetResult();

            if (app is not null)
            {
                var processConfiguration = _capabilityStore.ResolveAsync<ProcessConfiguration>
                (
                    "process", app, CancellationToken.None
                ).GetAwaiter().GetResult();

                if (processConfiguration is not null)
                {
                    maxStartupRetries = processConfiguration.MaxStartupRetries;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to resolve startup config for '{DisplayName}' -- using default max retries",
                process.DisplayName
            );
        }

        if (process.HasMaxStartupRetriesExceeded(maxStartupRetries))
        {
            previousState = process.MarkFatal();

            PublishStateChanged(process, previousState);

            _logger.LogError
            (
                "App '{DisplayName}' failed to start {Attempts} times -- manual restart required",
                process.DisplayName,
                process.StartupFailures
            );

            try
            {
                _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppFatal,
                        ActorId = ActivityActor.SystemId,
                        ActorName = ActivityActor.SystemName,
                        AppId = appId.ToString(null, CultureInfo.InvariantCulture),
                        AppSlug = process.AppSlug,
                        MetadataJson = JsonSerializer.Serialize(new { failureCount = process.StartupFailures })
                    },
                    CancellationToken.None
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", process.DisplayName);
            }

            return;
        }

        // Schedule startup retry with linear delay
        var delay = process.GetStartupRetryDelay();

        _logger.LogInformation
        (
            "App '{DisplayName}' will retry startup in {Delay}s",
            process.DisplayName,
            delay.TotalSeconds
        );

        var cancellation = new CancellationTokenSource();
        process.SetRestartDelayCancellation(cancellation);

        // Capture the token, not the source. CancelPendingRestart() disposes the source
        // (operator stop / process replace); a closure that reads cancellation.Token after
        // disposal throws ObjectDisposedException, which masks the real failure as
        // "Failed to retry startup". A CancellationToken value stays valid post-dispose --
        // a cancelled token simply trips the OperationCanceledException catch below. (#312)
        var token = cancellation.Token;

        // Startup retry is intentionally fire-and-forget from a synchronous event callback (Exited event).
        // The task is self-contained with full error handling and the CancellationTokenSource
        // is tracked on ManagedProcess for cancellation on operator stop.
#pragma warning disable VSTHRD110, MA0134
        Task.Run
        (
            async () =>
            {
                try
                {
                    await Task.Delay(delay, token);

                    // Remove the old process BEFORE disposing it -- dispose invalidates
                    // the semaphore, so we must not hold its lock during disposal.
                    _processes.TryRemove(appId, out var stale);

                    stale?.Dispose();

                    await StartAppInternalAsync(appId, token);
                }
                catch (OperationCanceledException)
                {
#pragma warning disable S6667 // OperationCanceledException is not a failure
                    _logger.LogDebug("Startup retry cancelled for '{DisplayName}'", process.DisplayName);
#pragma warning restore S6667
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to retry startup for '{DisplayName}'", process.DisplayName);
                }
            },
            token
        );
#pragma warning restore VSTHRD110, MA0134
    }
#pragma warning restore VSTHRD002

    // Called from synchronous Exited event callback -- sync-over-async is intentional
#pragma warning disable VSTHRD002
    private void OnRuntimeCrash(Ulid appId, ManagedProcess process, int exitCode)
    {
        // Check if exit code is a success code (clean shutdown, not a crash)
        var successExitCodes = new[] { 0 };

        try
        {
            var app = _appStore.GetByIdAsync(appId, CancellationToken.None).GetAwaiter().GetResult();

            if (app is not null)
            {
                var restartConfiguration = _capabilityStore.ResolveAsync<RestartConfiguration>
                (
                    "restart", app, CancellationToken.None
                ).GetAwaiter().GetResult();

                if (restartConfiguration?.SuccessExitCodes is not null)
                {
                    successExitCodes = restartConfiguration.SuccessExitCodes;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to resolve restart config for '{DisplayName}' -- using default success exit codes",
                process.DisplayName
            );
        }

        if (successExitCodes.Contains(exitCode))
        {
            var previous = process.MarkStopped();

            PublishStateChanged(process, previous);

            _logger.LogInformation
            (
                "App '{DisplayName}' exited with success code {ExitCode}",
                process.DisplayName,
                exitCode
            );

            return;
        }

        var previousState = process.MarkCrashed(exitCode);

        PublishStateChanged(process, previousState);

        _logger.LogWarning
        (
            "App '{DisplayName}' crashed with exit code {ExitCode}",
            process.DisplayName,
            exitCode
        );

        try
        {
            _activityEventStore.RecordAsync
            (
                new ActivityEvent
                {
                    EventType = ActivityEventTypes.AppCrashed,
                    ActorId = ActivityActor.SystemId,
                    ActorName = ActivityActor.SystemName,
                    AppId = appId.ToString(null, CultureInfo.InvariantCulture),
                    AppSlug = process.AppSlug,
                    MetadataJson = JsonSerializer.Serialize(new { exitCode })
                },
                CancellationToken.None
            ).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", process.DisplayName);
        }

        _restartPolicies.TryGetValue(appId, out var restartPolicy);

        var shouldRestart = restartPolicy switch
        {
            RestartPolicy.Always => true,
            RestartPolicy.OnCrash => true,
            _ => false
        };

        if (!shouldRestart)
        {
            _logger.LogInformation
            (
                "Restart policy '{RestartPolicy}' for '{DisplayName}' -- not restarting",
                restartPolicy,
                process.DisplayName
            );

            return;
        }

        if (process.HasMaxRestartsExceeded())
        {
            previousState = process.MarkFatal();

            PublishStateChanged(process, previousState);

            _logger.LogError
            (
                "App '{DisplayName}' has exceeded maximum restart count -- manual restart required",
                process.DisplayName
            );

            try
            {
                _activityEventStore.RecordAsync
                (
                    new ActivityEvent
                    {
                        EventType = ActivityEventTypes.AppFatal,
                        ActorId = ActivityActor.SystemId,
                        ActorName = ActivityActor.SystemName,
                        AppId = appId.ToString(null, CultureInfo.InvariantCulture),
                        AppSlug = process.AppSlug,
                        MetadataJson = JsonSerializer.Serialize(new { failureCount = process.RestartCount })
                    },
                    CancellationToken.None
                ).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", process.DisplayName);
            }

            return;
        }

        var delay = process.GetBackoffDelay();

        _logger.LogInformation
        (
            "Restart policy '{RestartPolicy}' for '{DisplayName}' -- restarting after {Delay}s",
            restartPolicy,
            process.DisplayName,
            delay.TotalSeconds
        );

        previousState = process.MarkRestarting();

        PublishStateChanged(process, previousState);

        var cancellation = new CancellationTokenSource();
        process.SetRestartDelayCancellation(cancellation);

        // Capture the token, not the source. CancelPendingRestart() disposes the source
        // (operator stop / process replace); a closure that reads cancellation.Token after
        // disposal throws ObjectDisposedException, which masks the real failure as
        // "Failed to restart". A CancellationToken value stays valid post-dispose --
        // a cancelled token simply trips the OperationCanceledException catch below. (#312)
        var token = cancellation.Token;

        // Restart is intentionally fire-and-forget from a synchronous event callback (Exited event).
        // The task is self-contained with full error handling and the CancellationTokenSource
        // is tracked on ManagedProcess for cancellation on operator stop.
#pragma warning disable VSTHRD110, MA0134
        Task.Run
        (
            async () =>
            {
                try
                {
                    await Task.Delay(delay, token);

                    // Remove the old process BEFORE disposing it -- dispose invalidates
                    // the semaphore, so we must not hold its lock during disposal.
                    _processes.TryRemove(appId, out var stale);

                    stale?.Dispose();

                    await StartAppInternalAsync(appId, token);

                    try
                    {
                        await _activityEventStore.RecordAsync
                        (
                            new ActivityEvent
                            {
                                EventType = ActivityEventTypes.AppAutoRestarted,
                                ActorId = ActivityActor.SystemId,
                                ActorName = ActivityActor.SystemName,
                                AppId = appId.ToString(null, CultureInfo.InvariantCulture),
                                AppSlug = process.AppSlug,
                                MetadataJson = JsonSerializer.Serialize
                                (
                                    new { restartCount = process.RestartCount, exitCode }
                                )
                            },
                            CancellationToken.None
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to record activity event for '{DisplayName}'", process.DisplayName);
                    }
                }
                catch (OperationCanceledException)
                {
#pragma warning disable S6667 // OperationCanceledException is not a failure
                    _logger.LogDebug("Restart cancelled for '{DisplayName}'", process.DisplayName);
#pragma warning restore S6667
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to restart '{DisplayName}'", process.DisplayName);
                }
            },
            token
        );
#pragma warning restore VSTHRD110, MA0134
    }
#pragma warning restore VSTHRD002

    private async Task StopProcessWithShutdownPolicyAsync(Ulid appId, ManagedProcess process, CancellationToken cancellationToken)
    {
        var shutdownTimeoutSeconds = 10;

        try
        {
            var app = await _appStore.GetByIdAsync(appId, cancellationToken);

            if (app is not null)
            {
                var processConfiguration = await _capabilityStore.ResolveAsync<ProcessConfiguration>
                (
                    "process", app, cancellationToken
                );

                if (processConfiguration is not null)
                {
                    shutdownTimeoutSeconds = processConfiguration.ShutdownTimeoutSeconds;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to resolve shutdown config for '{DisplayName}' -- using default timeout",
                process.DisplayName
            );
        }

        var previousState = process.MarkStopping();

        PublishStateChanged(process, previousState);

        await SendGracefulShutdownAsync(process, shutdownTimeoutSeconds, cancellationToken);

        previousState = process.MarkStopped();

        PublishStateChanged(process, previousState);
    }

    private async Task SendGracefulShutdownAsync(ManagedProcess process, int shutdownTimeoutSeconds, CancellationToken cancellationToken)
    {
        _logger.LogInformation
        (
            "Attempting graceful shutdown for '{DisplayName}' (timeout: {Timeout}s)",
            process.DisplayName,
            shutdownTimeoutSeconds
        );

        try
        {
            var signalSent = process.TryGracefulShutdown();

            if (!signalSent)
            {
                _logger.LogWarning
                (
                    "Could not send graceful shutdown signal to '{DisplayName}' -- hard killing",
                    process.DisplayName
                );
                process.KillProcess();
                return;
            }

            _logger.LogDebug("Graceful shutdown signal sent to '{DisplayName}'", process.DisplayName);

            // Link the host's shutdown token with the per-app shutdown timeout.
            // Whichever fires first trips the polling loop and falls through to hard kill,
            // so supervisor shutdown is bounded by min(host budget, per-app timeout). (#191)
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellation.CancelAfter(TimeSpan.FromSeconds(shutdownTimeoutSeconds));

            try
            {
                while (!timeoutCancellation.Token.IsCancellationRequested)
                {
                    if (process.HasProcessExited)
                    {
                        _logger.LogInformation("App '{DisplayName}' exited gracefully", process.DisplayName);
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Either the host budget elapsed or the per-app timeout fired -- fall through to hard kill
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Graceful shutdown error for '{DisplayName}' -- hard killing",
                process.DisplayName
            );
        }

        _logger.LogInformation
        (
            "Graceful shutdown timed out for '{DisplayName}' -- hard killing",
            process.DisplayName
        );

        process.KillProcess();
    }

    private void CheckGracePeriods(object? state)
    {
        foreach (var (appId, process) in _processes)
        {
            if (process.ShouldResetRestartCount())
            {
                process.ResetRestartCount();

                _logger.LogDebug
                (
                    "Reset restart count for '{DisplayName}' after grace period",
                    process.DisplayName
                );
            }

            // Reconciliation: detect processes that died without the supervisor noticing
            if (process.State == ProcessState.Running && process.HasProcessExited)
            {
                _logger.LogWarning
                (
                    "Reconciliation: process '{Slug}' is marked Running but has exited",
                    process.AppSlug
                );

                OnProcessExited(appId, process.HandleExitCode ?? -1);
            }
        }
    }

    private void PublishStateChanged(ManagedProcess process, ProcessState previousState) =>
        _eventBus.Publish
        (
            new ProcessStateChangedEvent
            (
                process.AppId,
                process.AppSlug,
                previousState,
                process.State,
                process.Port
            )
        );

    public void Dispose()
    {
        _graceTimer?.Dispose();

        foreach (var (_, process) in _processes)
        {
            process.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    // Parks an app in a non-running terminal state with an operator-facing reason
    // in its log buffer, without ever launching a process. Used for start-time
    // preconditions that fail before spawn (missing artifact, pinned-port
    // collision). The default Stopped state matches the historical
    // configuration-error parks. Pass ProcessState.Fatal for a hard precondition
    // failure that must not be masked by the restart cycle -- nothing was started,
    // so there is no exit event to drive a retry, and Fatal surfaces "manual
    // restart required" to the operator while still allowing a deliberate retry
    // once the conflict is cleared.
    private void ParkErrorProcess
    (
        Ulid appId,
        App app,
        string errorMessage,
        ProcessState state = ProcessState.Stopped
    )
    {
        var errorProcess = new ManagedProcess(app.Id, app.Slug, app.DisplayName);

        if (state == ProcessState.Fatal)
        {
            errorProcess.MarkFatal();
        }

        GetOrCreateLogBuffer(app.Id).Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, errorMessage));

        _processes[appId] = errorProcess;
    }

    // Returns the set of environment-variable keys the operator explicitly set on
    // this app's environment-defaults capability override. Used to scope shadow
    // warnings to operator-opt-in keys -- type-level defaults that happen to share
    // a key with a provider contribution are not foot-guns, but operator-dashboard
    // keys silently overridden by the host process env are. (See card #253 / #215.)
    private async Task<FrozenSet<string>> GetEnvironmentOverrideKeysAsync(Ulid appId, CancellationToken ct)
    {
        var overrides = await _appStore.GetOverridesAsync(appId, ct);

        if (!overrides.TryGetValue("environment-defaults", out var capabilityOverride))
        {
            return [];
        }

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(capabilityOverride.ConfigurationJson);
        }
        catch (JsonException)
        {
            // Malformed override JSON shouldn't crash the spawn -- the resolver
            // will surface the same problem more legibly. Treat as "no override
            // keys" so the warning system stays quiet on this app.
            return [];
        }

        var variables = root?["variables"]?.AsObject();

        if (variables is null || variables.Count == 0)
        {
            return [];
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (key, _) in variables)
        {
            keys.Add(key);
        }

        return keys.ToFrozenSet(StringComparer.Ordinal);
    }

    // Builds the merged environment dictionary the supervisor hands to the runner.
    // Type-default + operator-override values land first (the resolved capability
    // config); environment providers then contribute and win on key conflict.
    //
    // When a provider's contribution overrides a key the operator explicitly set
    // via the dashboard with a different value, emit one warning per shadowed
    // key per spawn. This catches the dual-path-different-values foot-gun: an
    // operator updates the dashboard but forgets to remove a systemd drop-in
    // (or shell wrapper) that injects the same env var into the host process.
    // (Card #253; recon source: card #215 Q3.)
    internal static Dictionary<string, string> MergeEnvironmentVariables
    (
        IDictionary<string, string>? capabilityVariables,
        FrozenSet<string> operatorOverrideKeys,
        IReadOnlyList<IProcessEnvironmentProvider> providers,
        string appSlug,
        ILogger logger
    )
    {
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);

        if (capabilityVariables is not null)
        {
            foreach (var (key, value) in capabilityVariables)
            {
                environmentVariables[key] = value;
            }
        }

        // Allow subsystems to contribute environment variables at start time.
        // This is how the proxy subsystem flows secrets (e.g. the DNS API token)
        // from Collabhost's own host process env into the Caddy child without
        // ever persisting them to the database. Provider contributions win over
        // capability defaults on key conflict -- the provider is the source of
        // truth for any key it contributes.
        foreach (var provider in providers)
        {
            var contributions = provider.ContributeEnvironment(appSlug);

            foreach (var (key, value) in contributions)
            {
                if (operatorOverrideKeys.Contains(key)
                    && environmentVariables.TryGetValue(key, out var existing)
                    && !string.Equals(existing, value, StringComparison.Ordinal))
                {
                    logger.LogWarning
                    (
                        "Environment variable '{Key}' provided by {ProviderName} shadows the capability override value. Provider value used.",
                        key,
                        provider.GetType().Name
                    );
                }

                environmentVariables[key] = value;
            }
        }

        return environmentVariables;
    }
}
