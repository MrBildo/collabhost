using System.Collections.Concurrent;
using System.Globalization;

using Collabhost.Api.Capabilities.Configurations;
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
    IEventBus<ProcessStateChangedEvent> eventBus,
    ILogger<ProcessSupervisor> logger
) : IHostedService, IDisposable
{
    private readonly IManagedProcessRunner _runner = runner
        ?? throw new ArgumentNullException(nameof(runner));

    private readonly IProcessContainment _containment = containment
        ?? throw new ArgumentNullException(nameof(containment));

    private readonly AppStore _appStore = appStore
        ?? throw new ArgumentNullException(nameof(appStore));

    private readonly IEventBus<ProcessStateChangedEvent> _eventBus = eventBus
        ?? throw new ArgumentNullException(nameof(eventBus));

    private readonly ILogger<ProcessSupervisor> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    private readonly ConcurrentDictionary<Ulid, ManagedProcess> _processes = new();
    private readonly ConcurrentDictionary<Ulid, RestartPolicy> _restartPolicies = new();
    private Timer? _graceTimer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor starting -- checking for auto-start apps");

        _graceTimer = new Timer(CheckGracePeriods, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        try
        {
            var apps = await _appStore.ListAsync(cancellationToken);

            foreach (var app in apps)
            {
                var autoStartConfiguration = await _appStore.ResolveCapabilityAsync<AutoStartConfiguration>
                (
                    app.AppTypeId, app.Id, "auto-start", cancellationToken
                );

                if (autoStartConfiguration is null || !autoStartConfiguration.Enabled)
                {
                    continue;
                }

                var hasProcess = await _appStore.HasBindingAsync
                (
                    app.AppTypeId, "process", cancellationToken
                );

                if (!hasProcess)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Auto-starting app '{DisplayName}'", app.DisplayName);

                    await StartAppInternalAsync(app.Id, cancellationToken);
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

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor stopping -- stopping all managed processes");

        if (_graceTimer is not null)
        {
            await _graceTimer.DisposeAsync();
        }

        var stopTasks = _processes.Select
        (
            async kvp =>
            {
                var (appId, process) = kvp;

                await using var _ = await process.AcquireOperationLockAsync(cancellationToken);

                if (process.IsRunning)
                {
                    await StopProcessWithShutdownPolicyAsync(appId, process);

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

        _logger.LogInformation("Process supervisor stopped");
    }

    public async Task<ManagedProcess> StartAppAsync(Ulid appId, CancellationToken ct = default)
    {
        if (_processes.TryGetValue(appId, out var existing))
        {
            await using var _ = await existing.AcquireOperationLockAsync(ct);

            return await StartAppInternalAsync(appId, ct);
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

        await StopProcessWithShutdownPolicyAsync(appId, process);

        _logger.LogInformation("Stopped app '{DisplayName}'", process.DisplayName);

        return process;
    }

    public async Task<ManagedProcess> RestartAppAsync(Ulid appId, CancellationToken ct = default)
    {
        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            await using var operationLock = await existing.AcquireOperationLockAsync(ct);

            existing.ClearStoppedByOperator();

            await StopProcessWithShutdownPolicyAsync(appId, existing);

            existing.Dispose();
            _processes.TryRemove(appId, out _);
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

    private async Task<ManagedProcess> StartAppInternalAsync(Ulid appId, CancellationToken ct)
    {
        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            throw new InvalidOperationException("App is already running.");
        }

        var app = await _appStore.GetByIdAsync(appId, ct)
            ?? throw new InvalidOperationException("App not found.");

        var hasProcess = await _appStore.HasBindingAsync(app.AppTypeId, "process", ct);

        if (!hasProcess)
        {
            throw new InvalidOperationException("This app type does not have a process capability.");
        }

        var processConfiguration = await _appStore.ResolveCapabilityAsync<ProcessConfiguration>
        (
            app.AppTypeId, app.Id, "process", ct
        ) ?? throw new InvalidOperationException("Process capability configuration could not be resolved.");

        var artifactConfiguration = await _appStore.ResolveCapabilityAsync<ArtifactConfiguration>
        (
            app.AppTypeId, app.Id, "artifact", ct
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

        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);

        var environmentConfiguration = await _appStore.ResolveCapabilityAsync<EnvironmentConfiguration>
        (
            app.AppTypeId, app.Id, "environment-defaults", ct
        );

        if (environmentConfiguration?.Variables is not null)
        {
            foreach (var (key, value) in environmentConfiguration.Variables)
            {
                environmentVariables[key] = value;
            }
        }

        var managed = new ManagedProcess(app.Id, app.Slug, app.DisplayName);

        var portInjectionConfiguration = await _appStore.ResolveCapabilityAsync<PortInjectionConfiguration>
        (
            app.AppTypeId, app.Id, "port-injection", ct
        );

        if (portInjectionConfiguration is not null)
        {
            var portAllocator = new PortAllocator();
            var allocatedPort = await portAllocator.AllocateAsync(ct);

            managed.AssignPort(allocatedPort);

            var formattedPortValue = portInjectionConfiguration.PortFormat
                .Replace
                (
                    "{port}",
                    allocatedPort.ToString(CultureInfo.InvariantCulture),
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
            (line, stream) => managed.LogBuffer.Add(new LogEntry(DateTime.UtcNow, stream, line))
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

        previousState = managed.MarkRunning(handle);

        PublishStateChanged(managed, previousState);

        var restartConfiguration = await _appStore.ResolveCapabilityAsync<RestartConfiguration>
        (
            app.AppTypeId, app.Id, "restart", ct
        );

        _restartPolicies[appId] = restartConfiguration?.Policy ?? RestartPolicy.Never;

        handle.Exited += exitCode => OnProcessExited(appId, exitCode);

        _processes.TryRemove(appId, out var old);
        old?.Dispose();

        _processes[appId] = managed;

        _logger.LogInformation
        (
            "Started app '{DisplayName}' (PID {Pid}, Port {Port})",
            app.DisplayName,
            handle.Pid,
            managed.Port?.ToString(CultureInfo.InvariantCulture) ?? "none"
        );

        return managed;
    }

    private void OnProcessExited(Ulid appId, int exitCode)
    {
        if (!_processes.TryGetValue(appId, out var process))
        {
            return;
        }

        if (process.State is ProcessState.Stopping or ProcessState.Stopped)
        {
            return;
        }

        var previousState = process.MarkCrashed();

        PublishStateChanged(process, previousState);

        _logger.LogWarning
        (
            "App '{DisplayName}' exited with code {ExitCode}",
            process.DisplayName,
            exitCode
        );

        if (process.StoppedByOperator)
        {
            _logger.LogInformation
            (
                "App '{DisplayName}' was stopped by operator -- skipping restart",
                process.DisplayName
            );
            return;
        }

        _restartPolicies.TryGetValue(appId, out var restartPolicy);

        var shouldRestart = restartPolicy switch
        {
            RestartPolicy.Always => true,
            RestartPolicy.OnCrash => exitCode != 0,
            _ => false
        };

        if (!shouldRestart)
        {
            _logger.LogInformation
            (
                "Restart policy '{RestartPolicy}' for '{DisplayName}' -- not restarting (exit code: {ExitCode})",
                restartPolicy,
                process.DisplayName,
                exitCode
            );
            return;
        }

        if (process.HasMaxRestartsExceeded())
        {
            _logger.LogError
            (
                "App '{DisplayName}' has exceeded maximum restart count -- not restarting",
                process.DisplayName
            );
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
                    await Task.Delay(delay, cancellation.Token);

                    await using var _ = await process.AcquireOperationLockAsync(cancellation.Token);

                    _processes.TryRemove(appId, out var stale);
                    stale?.Dispose();

                    await StartAppInternalAsync(appId, cancellation.Token);
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
            cancellation.Token
        );
#pragma warning restore VSTHRD110, MA0134
    }

    private async Task StopProcessWithShutdownPolicyAsync(Ulid appId, ManagedProcess process)
    {
        var gracefulShutdown = false;
        var shutdownTimeoutSeconds = 10;

        try
        {
            var app = await _appStore.GetByIdAsync(appId, CancellationToken.None);

            if (app is not null)
            {
                var processConfiguration = await _appStore.ResolveCapabilityAsync<ProcessConfiguration>
                (
                    app.AppTypeId, app.Id, "process", CancellationToken.None
                );

                if (processConfiguration is not null)
                {
                    gracefulShutdown = processConfiguration.GracefulShutdown;
                    shutdownTimeoutSeconds = processConfiguration.ShutdownTimeoutSeconds;
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to resolve shutdown config for '{DisplayName}' -- falling back to hard kill",
                process.DisplayName
            );
        }

        var previousState = process.MarkStopping();

        PublishStateChanged(process, previousState);

        if (gracefulShutdown)
        {
            await SendGracefulShutdownAsync(process, shutdownTimeoutSeconds);
        }
        else
        {
            process.KillProcess();
        }

        previousState = process.MarkStopped();

        PublishStateChanged(process, previousState);
    }

    private async Task SendGracefulShutdownAsync(ManagedProcess process, int shutdownTimeoutSeconds)
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

            using var timeoutCancellation = new CancellationTokenSource
            (
                TimeSpan.FromSeconds(shutdownTimeoutSeconds)
            );

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
                // Timeout reached -- fall through to hard kill
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
        foreach (var (_, process) in _processes)
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

    private void ParkErrorProcess(Ulid appId, App app, string errorMessage)
    {
        var errorProcess = new ManagedProcess(app.Id, app.Slug, app.DisplayName);

        errorProcess.LogBuffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, errorMessage));

        _processes[appId] = errorProcess;
    }
}
