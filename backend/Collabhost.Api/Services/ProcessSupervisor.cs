using System.Collections.Concurrent;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Services;

public class ProcessSupervisor
(
    IManagedProcessRunner runner,
    IServiceScopeFactory scopeFactory,
    IProcessStateEventBus processStateEventBus,
    ILogger<ProcessSupervisor> logger
) : IHostedService, IDisposable
{
    private readonly IManagedProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    // ProcessSupervisor is a Singleton (holds in-memory process state across requests).
    // Singletons cannot inject Scoped services like DbContext directly,
    // so we use IServiceScopeFactory to create short-lived scopes when DB access is needed.
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly IProcessStateEventBus _processStateEventBus = processStateEventBus ?? throw new ArgumentNullException(nameof(processStateEventBus));
    private readonly ILogger<ProcessSupervisor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _graceTimer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor starting — checking for auto-start apps");

        _graceTimer = new Timer(CheckGracePeriods, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        // TODO: Card #39 — auto-start via capability resolver instead of App.AutoStart column
        _logger.LogInformation("Process supervisor started — auto-start deferred to Card #39");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor stopping — killing all managed processes");

        if (_graceTimer is not null)
        {
            await _graceTimer.DisposeAsync();
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var (_, process) in _processes)
            {
                if (process.IsRunning)
                {
                    var previousStateId = process.ProcessStateId;
                    process.MarkStopping();
                    PublishStateChanged(process, previousStateId);

                    process.KillProcess();

                    previousStateId = process.ProcessStateId;
                    process.MarkStopped();
                    PublishStateChanged(process, previousStateId);

                    _logger.LogInformation("Stopped app '{AppName}' (PID {Pid})", process.AppName, process.Pid);
                }

                process.Dispose();
            }

            _processes.Clear();
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

            var previousStateId = process.ProcessStateId;
            process.MarkStopping();
            PublishStateChanged(process, previousStateId);

            process.KillProcess();

            previousStateId = process.ProcessStateId;
            process.MarkStopped();
            PublishStateChanged(process, previousStateId);

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
                var previousStateId = existing.ProcessStateId;
                existing.MarkStopping();
                PublishStateChanged(existing, previousStateId);

                existing.KillProcess();

                previousStateId = existing.ProcessStateId;
                existing.MarkStopped();
                PublishStateChanged(existing, previousStateId);

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

    private async Task<ManagedProcess> StartAppInternalAsync(Guid appId, CancellationToken ct)
    {
        if (_processes.TryGetValue(appId, out var existing) && existing.IsRunning)
        {
            throw new InvalidOperationException("App is already running.");
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        var app = await db.Apps
            .SingleOrDefaultAsync(a => a.Id == appId, ct) ?? throw new InvalidOperationException("App not found.");

        // TODO: Card #39 — use capability resolver for process discovery, env vars, port injection
        // For now, use a minimal stub that will allow process start for testing
        var hasProcess = await db.HasCapabilityAsync(app.AppTypeId, IdentifierCatalog.Capabilities.Process, ct);

        if (!hasProcess)
        {
            throw new InvalidOperationException("This app type does not have a process capability.");
        }

        // TODO: Card #39 — resolve env vars and port from capability resolver
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);

        var managed = new ManagedProcess(app.Id, app.ExternalId, app.DisplayName);

        var previousStateId = managed.ProcessStateId;
        managed.MarkStarting();
        PublishStateChanged(managed, previousStateId);

        // TODO: Card #39 — resolve command and working directory from process capability discovery strategy
        var config = new ProcessStartConfig
        (
            "stub-command",
            null,
            null,
            environmentVariables,
            (line, stream) => managed.LogBuffer.Add(new LogEntry(DateTime.UtcNow, stream, line))
        );

        var handle = _runner.Start(config);

        previousStateId = managed.ProcessStateId;
        managed.MarkRunning(handle);
        PublishStateChanged(managed, previousStateId);

        // TODO: Card #39 — resolve restart policy from capability
        handle.Exited += exitCode => OnProcessExited(appId, exitCode);

        _processes.TryRemove(appId, out var old);
        old?.Dispose();

        _processes[appId] = managed;

        _logger.LogInformation
        (
            "Started app '{AppName}' (PID {Pid})",
            app.Name,
            handle.Pid
        );

        return managed;
    }

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

        // TODO: Card #39 — resolve restart policy from capability resolver
        var previousStateId = process.ProcessStateId;
        process.MarkCrashed();
        PublishStateChanged(process, previousStateId);

        _logger.LogWarning
        (
            "App '{AppName}' exited with code {ExitCode}",
            process.AppName,
            exitCode
        );
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
