using System.Collections.Concurrent;

using Collabhost.Api.Domain.Capabilities;
using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Services;

public sealed class ProcessSupervisor
(
    IManagedProcessRunner runner,
    IServiceScopeFactory scopeFactory,
    IProcessStateEventBus processStateEventBus,
    DiscoveryStrategyFactory discoveryStrategyFactory,
    ILogger<ProcessSupervisor> logger
) : IHostedService, IDisposable
{
    private readonly IManagedProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    // ProcessSupervisor is a Singleton (holds in-memory process state across requests).
    // Singletons cannot inject Scoped services like DbContext directly,
    // so we use IServiceScopeFactory to create short-lived scopes when DB access is needed.
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IProcessStateEventBus _processStateEventBus = processStateEventBus ?? throw new ArgumentNullException(nameof(processStateEventBus));
    private readonly DiscoveryStrategyFactory _discoveryStrategyFactory = discoveryStrategyFactory ?? throw new ArgumentNullException(nameof(discoveryStrategyFactory));
    private readonly ILogger<ProcessSupervisor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();
    private readonly ConcurrentDictionary<Guid, string> _restartPolicies = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _graceTimer;

#pragma warning disable MA0051 // Long method justified — startup with auto-start capability resolution
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor starting — checking for auto-start apps");

        _graceTimer = new Timer(CheckGracePeriods, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var capabilityResolver = scope.ServiceProvider.GetRequiredService<ICapabilityResolver>();
            var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

            var apps = await db.Apps
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var app in apps)
            {
                var autoStartConfiguration = await capabilityResolver.ResolveAsync<AutoStartConfiguration>
                (
                    app.Id, IdentifierCatalog.Capabilities.AutoStart, cancellationToken
                );

                if (autoStartConfiguration is null || !autoStartConfiguration.Enabled)
                {
                    continue;
                }

                var hasProcess = await db.HasCapabilityAsync
                (
                    app.AppTypeId, IdentifierCatalog.Capabilities.Process, cancellationToken
                );

                if (!hasProcess)
                {
                    continue;
                }

                try
                {
                    _logger.LogInformation("Auto-starting app '{AppName}'", app.DisplayName);
                    await StartAppInternalAsync(app.Id, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError
                    (
                        exception,
                        "Failed to auto-start app '{AppName}'",
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
#pragma warning restore MA0051

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor stopping — stopping all managed processes");

        if (_graceTimer is not null)
        {
            await _graceTimer.DisposeAsync();
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var (appId, process) in _processes)
            {
                if (process.IsRunning)
                {
                    await StopProcessWithShutdownPolicyAsync(appId, process);
                    _logger.LogInformation("Stopped app '{AppName}' (PID {Pid})", process.AppName, process.Pid);
                }

                process.Dispose();
            }

            _processes.Clear();
            _restartPolicies.Clear();
        }
        finally
        {
            _lock.Release();
        }

        _logger.LogInformation("Process supervisor stopped");
    }

    public async Task<ManagedProcess> StartAppAsync(Guid appId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await StartAppInternalAsync(appId, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ManagedProcess> StopAppAsync(Guid appId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_processes.TryGetValue(appId, out var process) || process.IsStopped)
            {
                throw new InvalidOperationException("App is already stopped.");
            }

            process.MarkStoppedByOperator();

            await StopProcessWithShutdownPolicyAsync(appId, process);

            _logger.LogInformation("Stopped app '{AppName}'", process.AppName);

            return process;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ManagedProcess> RestartAppAsync(Guid appId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
            {
                existing.ClearStoppedByOperator();

                await StopProcessWithShutdownPolicyAsync(appId, existing);

                existing.Dispose();
                _processes.TryRemove(appId, out _);
            }

            return await StartAppInternalAsync(appId, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public ManagedProcess? GetProcess(Guid appId)
    {
        _processes.TryGetValue(appId, out var process);
        return process;
    }

#pragma warning disable MA0051 // Long method justified — process start with full capability resolution
    private async Task<ManagedProcess> StartAppInternalAsync(Guid appId, CancellationToken ct)
    {
        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            throw new InvalidOperationException("App is already running.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();
        var capabilityResolver = scope.ServiceProvider.GetRequiredService<ICapabilityResolver>();

        var app = await db.Apps
            .SingleOrDefaultAsync(a => a.Id == appId, ct)
            ?? throw new InvalidOperationException("App not found.");

        var hasProcess = await db.HasCapabilityAsync
        (
            app.AppTypeId, IdentifierCatalog.Capabilities.Process, ct
        );

        if (!hasProcess)
        {
            throw new InvalidOperationException("This app type does not have a process capability.");
        }

        // Resolve process configuration for discovery strategy
        var processConfiguration = await capabilityResolver.ResolveAsync<ProcessConfiguration>
        (
            app.Id, IdentifierCatalog.Capabilities.Process, ct
        )
            ?? throw new InvalidOperationException("Process capability configuration could not be resolved.");

        // Resolve artifact configuration for working directory
        var artifactConfiguration = await capabilityResolver.ResolveAsync<ArtifactConfiguration>
        (
            app.Id, IdentifierCatalog.Capabilities.Artifact, ct
        );

        if (artifactConfiguration is null || string.IsNullOrWhiteSpace(artifactConfiguration.Location))
        {
            var errorMessage = "Cannot start app: artifact location is not configured. Set the artifact capability's location field.";
            var errorProcess = new ManagedProcess(app.Id, app.ExternalId, app.DisplayName);
            errorProcess.LogBuffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, errorMessage));
            _processes[appId] = errorProcess;
            throw new InvalidOperationException(errorMessage);
        }

        if (!Directory.Exists(artifactConfiguration.Location))
        {
            var errorMessage = $"Cannot start app: artifact location '{artifactConfiguration.Location}' does not exist on disk.";
            var errorProcess = new ManagedProcess(app.Id, app.ExternalId, app.DisplayName);
            errorProcess.LogBuffer.Add(new LogEntry(DateTime.UtcNow, LogStream.StdErr, errorMessage));
            _processes[appId] = errorProcess;
            throw new InvalidOperationException(errorMessage);
        }

        // Compute effective working directory from artifact + optional relative process working directory
        var effectiveWorkingDirectory = !string.IsNullOrWhiteSpace(processConfiguration.WorkingDirectory)
            ? Path.Combine(artifactConfiguration.Location, processConfiguration.WorkingDirectory)
            : artifactConfiguration.Location;

        // Discover the command/args/working directory using the appropriate strategy
        var strategy = _discoveryStrategyFactory.GetStrategy(processConfiguration.DiscoveryStrategy);
        var discoveredProcess = strategy.Discover(processConfiguration, effectiveWorkingDirectory);

        // Build environment variables from environment-defaults capability
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);

        var environmentDefaultsConfiguration = await capabilityResolver.ResolveAsync<EnvironmentDefaultsConfiguration>
        (
            app.Id, IdentifierCatalog.Capabilities.EnvironmentDefaults, ct
        );

        if (environmentDefaultsConfiguration?.Defaults is not null)
        {
            foreach (var (key, value) in environmentDefaultsConfiguration.Defaults)
            {
                environmentVariables[key] = value;
            }
        }

        var managed = new ManagedProcess(app.Id, app.ExternalId, app.DisplayName);

        // Port injection: allocate port and inject env var (port injection wins on name conflict)
        var portInjectionConfiguration = await capabilityResolver.ResolveAsync<PortInjectionConfiguration>
        (
            app.Id, IdentifierCatalog.Capabilities.PortInjection, ct
        );

        if (portInjectionConfiguration is not null)
        {
            var portAllocator = scope.ServiceProvider.GetRequiredService<PortAllocator>();
            var allocatedPort = await portAllocator.AllocateAsync(ct);
            managed.AssignPort(allocatedPort);

            var formattedPortValue = portInjectionConfiguration.PortFormat
                .Replace
                (
                    "{port}",
                    allocatedPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.OrdinalIgnoreCase
                );

            // Port injection wins on name conflict with environment defaults
            environmentVariables[portInjectionConfiguration.EnvironmentVariableName] = formattedPortValue;
        }

        var previousStateId = managed.ProcessStateId;
        managed.MarkStarting();
        managed.ClearStoppedByOperator();
        PublishStateChanged(managed, previousStateId);

        var config = new ProcessStartConfig
        (
            discoveredProcess.Command,
            discoveredProcess.Arguments,
            discoveredProcess.WorkingDirectory,
            environmentVariables,
            (line, stream) => managed.LogBuffer.Add(new LogEntry(DateTime.UtcNow, stream, line))
        );

        var handle = _runner.Start(config);

        previousStateId = managed.ProcessStateId;
        managed.MarkRunning(handle);
        PublishStateChanged(managed, previousStateId);

        // Resolve and store restart policy for use on process exit
        var restartConfiguration = await capabilityResolver.ResolveAsync<RestartConfiguration>
        (
            app.Id, IdentifierCatalog.Capabilities.Restart, ct
        );

        var restartPolicy = restartConfiguration?.Policy ?? StringCatalog.RestartPolicies.Never;
        _restartPolicies[appId] = restartPolicy;

        handle.Exited += exitCode => OnProcessExited(appId, exitCode);

        _processes.TryRemove(appId, out var old);
        old?.Dispose();

        _processes[appId] = managed;

        _logger.LogInformation
        (
            "Started app '{AppName}' (PID {Pid}, Port {Port})",
            app.Name,
            handle.Pid,
            managed.Port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"
        );

        return managed;
    }
#pragma warning restore MA0051

    private void OnProcessExited(Guid appId, int exitCode)
    {
        if (!_processes.TryGetValue(appId, out var process))
        {
            return;
        }

        if (process.ProcessStateId == IdentifierCatalog.ProcessStates.Stopping
            || process.ProcessStateId == IdentifierCatalog.ProcessStates.Stopped)
        {
            return;
        }

        var previousStateId = process.ProcessStateId;
        process.MarkCrashed();
        PublishStateChanged(process, previousStateId);

        _logger.LogWarning
        (
            "App '{AppName}' exited with code {ExitCode}",
            process.AppName,
            exitCode
        );

        // Do not restart if the operator explicitly stopped this process
        if (process.StoppedByOperator)
        {
            _logger.LogInformation
            (
                "App '{AppName}' was stopped by operator — skipping restart",
                process.AppName
            );
            return;
        }

        // Apply restart policy
        _restartPolicies.TryGetValue(appId, out var restartPolicy);
        restartPolicy ??= StringCatalog.RestartPolicies.Never;

        var shouldRestart = restartPolicy switch
        {
            StringCatalog.RestartPolicies.Always => true,
            StringCatalog.RestartPolicies.OnCrash => exitCode != 0,
            _ => false
        };

        if (!shouldRestart)
        {
            _logger.LogInformation
            (
                "Restart policy '{RestartPolicy}' for '{AppName}' — not restarting (exit code: {ExitCode})",
                restartPolicy,
                process.AppName,
                exitCode
            );
            return;
        }

        if (process.HasMaxRestartsExceeded())
        {
            _logger.LogError
            (
                "App '{AppName}' has exceeded maximum restart count — not restarting",
                process.AppName
            );
            return;
        }

        var delay = process.GetBackoffDelay();
        _logger.LogInformation
        (
            "Restart policy '{RestartPolicy}' for '{AppName}' — restarting after {Delay}s",
            restartPolicy,
            process.AppName,
            delay.TotalSeconds
        );

        previousStateId = process.ProcessStateId;
        process.MarkRestarting();
        PublishStateChanged(process, previousStateId);

        var cancellation = new CancellationTokenSource();
        process.SetRestartDelayCancellation(cancellation);

        // Restart is intentionally fire-and-forget from an event callback (Exited event).
        // The task is self-contained with full error handling and the CancellationTokenSource
        // is tracked on ManagedProcess for cancellation on operator stop.
#pragma warning disable VSTHRD110, MA0134 // Intentional fire-and-forget restart from synchronous event callback
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cancellation.Token);
                await _lock.WaitAsync(cancellation.Token);
                try
                {
                    _processes.TryRemove(appId, out var stale);
                    stale?.Dispose();

                    await StartAppInternalAsync(appId, cancellation.Token);
                }
                finally
                {
                    _lock.Release();
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected when operator stops the app — not an error
#pragma warning disable S6667 // OperationCanceledException is not a failure, no need to log it
                _logger.LogDebug("Restart cancelled for '{AppName}'", process.AppName);
#pragma warning restore S6667
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to restart '{AppName}'", process.AppName);
            }
        }, cancellation.Token);
#pragma warning restore VSTHRD110, MA0134
    }

    private async Task StopProcessWithShutdownPolicyAsync(Guid appId, ManagedProcess process)
    {
        // Resolve graceful shutdown configuration
        var gracefulShutdown = false;
        var shutdownTimeoutSeconds = 10;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var capabilityResolver = scope.ServiceProvider.GetRequiredService<ICapabilityResolver>();

            var processConfiguration = await capabilityResolver.ResolveAsync<ProcessConfiguration>
            (
                appId, IdentifierCatalog.Capabilities.Process, CancellationToken.None
            );

            if (processConfiguration is not null)
            {
                gracefulShutdown = processConfiguration.GracefulShutdown;
                shutdownTimeoutSeconds = processConfiguration.ShutdownTimeoutSeconds;
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Failed to resolve shutdown config for '{AppName}' — falling back to hard kill",
                process.AppName
            );
        }

        var previousStateId = process.ProcessStateId;
        process.MarkStopping();
        PublishStateChanged(process, previousStateId);

        if (gracefulShutdown)
        {
            await SendGracefulShutdownAsync(process, shutdownTimeoutSeconds);
        }
        else
        {
            process.KillProcess();
        }

        previousStateId = process.ProcessStateId;
        process.MarkStopped();
        PublishStateChanged(process, previousStateId);
    }

    private async Task SendGracefulShutdownAsync(ManagedProcess process, int shutdownTimeoutSeconds)
    {
        _logger.LogInformation
        (
            "Attempting graceful shutdown for '{AppName}' (timeout: {Timeout}s)",
            process.AppName,
            shutdownTimeoutSeconds
        );

        try
        {
            var signalSent = process.TryGracefulShutdown();

            if (!signalSent)
            {
                _logger.LogWarning
                (
                    "Could not send graceful shutdown signal to '{AppName}' — hard killing",
                    process.AppName
                );
                process.KillProcess();
                return;
            }

            _logger.LogDebug("Graceful shutdown signal sent to '{AppName}'", process.AppName);

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
                        _logger.LogInformation("App '{AppName}' exited gracefully", process.AppName);
                        return;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout reached — fall through to hard kill
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning
            (
                exception,
                "Graceful shutdown error for '{AppName}' — hard killing",
                process.AppName
            );
        }

        _logger.LogInformation
        (
            "Graceful shutdown timed out for '{AppName}' — hard killing",
            process.AppName
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
                    "Reset restart count for '{AppName}' after grace period",
                    process.AppName
                );
            }
        }
    }

    private void PublishStateChanged(ManagedProcess process, Guid previousStateId) =>
        _processStateEventBus.Publish
        (
            new ProcessStateChangedEvent
            (
                process.AppId,
                process.AppExternalId,
                previousStateId,
                process.ProcessStateId
            )
        );

    public void Dispose()
    {
        _graceTimer?.Dispose();
        _lock.Dispose();

        foreach (var (_, process) in _processes)
        {
            process.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}
