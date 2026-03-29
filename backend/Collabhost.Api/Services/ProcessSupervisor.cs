using System.Collections.Concurrent;
using System.Globalization;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;

namespace Collabhost.Api.Services;

public class ProcessSupervisor
(
    IManagedProcessRunner runner,
    IServiceScopeFactory scopeFactory,
    ILogger<ProcessSupervisor> logger
) : IHostedService, IDisposable
{
    private readonly IManagedProcessRunner _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    private readonly ILogger<ProcessSupervisor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ConcurrentDictionary<Guid, ManagedProcess> _processes = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Timer? _graceTimer;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Process supervisor starting — checking for auto-start apps");

        _graceTimer = new Timer(CheckGracePeriods, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CollabhostDbContext>();

        var autoStartApps = await db.Database
            .SqlQuery<AutoStartApp>(
                $"""
                SELECT
                    A.[Id]
                    ,A.[ExternalId]
                    ,A.[Name]
                    ,A.[AppTypeId]
                FROM
                    [App] A
                WHERE
                    A.[AutoStart] = 1
                """)
            .ToListAsync(cancellationToken);

        var sortedApps = autoStartApps.OrderBy(a => AppTypeBehavior.StartupPriority(a.AppTypeId)).ToList();

        foreach (var app in sortedApps)
        {
            if (!AppTypeBehavior.HasProcess(app.AppTypeId))
            {
                _logger.LogDebug("Skipping auto-start for '{AppName}' — app type has no process", app.Name);
                continue;
            }

            try
            {
                await StartAppInternalAsync(app.Id, cancellationToken);
                _logger.LogInformation("Auto-started app '{AppName}'", app.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to auto-start app '{AppName}'", app.Name);
            }
        }

        _logger.LogInformation("Process supervisor started");
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
                    process.MarkStopping();
                    process.KillProcess();
                    process.MarkStopped();
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

            process.MarkStopping();
            process.KillProcess();
            process.MarkStopped();

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
                existing.MarkStopping();
                existing.KillProcess();
                existing.MarkStopped();
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

    public ManagedProcess? GetStatus(Guid appId)
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
            .Include(a => a.EnvironmentVariables)
            .SingleOrDefaultAsync(a => a.Id == appId, ct) ?? throw new InvalidOperationException("App not found.");
        if (!AppTypeBehavior.HasProcess(app.AppTypeId))
        {
            throw new InvalidOperationException("Static sites do not have a managed process.");
        }

        if (string.IsNullOrWhiteSpace(app.CommandLine))
        {
            throw new InvalidOperationException("App has no command line configured.");
        }

        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in app.EnvironmentVariables)
        {
            environmentVariables[variable.Name] = variable.Value;
        }

        if (app.Port.HasValue)
        {
            environmentVariables["PORT"] = app.Port.Value.ToString(CultureInfo.InvariantCulture);
        }

        var managed = new ManagedProcess(app.Id, app.ExternalId, app.DisplayName);
        managed.MarkStarting();

        var config = new ProcessStartConfig
        (
            app.CommandLine,
            app.Arguments,
            app.WorkingDirectory ?? app.InstallDirectory,
            environmentVariables,
            (line, stream) => managed.LogBuffer.Add(new LogEntry(DateTime.UtcNow, stream, line))
        );

        var handle = _runner.Start(config);
        managed.MarkRunning(handle);

        handle.Exited += exitCode => OnProcessExited(appId, app.RestartPolicyId, exitCode);

        _processes.TryRemove(appId, out var old);
        old?.Dispose();

        _processes[appId] = managed;

        _logger.LogInformation
        (
            "Started app '{AppName}' (PID {Pid}, Port {Port})",
            app.Name,
            handle.Pid,
            app.Port
        );

        return managed;
    }

    private void OnProcessExited(Guid appId, Guid restartPolicyId, int exitCode)
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

        var shouldRestart = restartPolicyId == IdentifierCatalog.RestartPolicies.Always
            || (restartPolicyId == IdentifierCatalog.RestartPolicies.OnCrash && exitCode != 0);

        process.MarkCrashed();
        _logger.LogWarning
        (
            "App '{AppName}' exited with code {ExitCode}",
            process.AppName,
            exitCode
        );

        if (shouldRestart && !process.HasMaxRestartsExceeded())
        {
            process.MarkRestarting();
            var delay = process.GetBackoffDelay();
            _logger.LogInformation
            (
                "Scheduling restart for '{AppName}' in {Delay}s (attempt {Attempt})",
                process.AppName,
                delay.TotalSeconds,
                process.RestartCount + 1
            );

            ScheduleRestart(appId, delay);
        }
        else if (process.HasMaxRestartsExceeded())
        {
            _logger.LogError
            (
                "App '{AppName}' exceeded max restart attempts — staying crashed",
                process.AppName
            );
        }
    }

    private void ScheduleRestart(Guid appId, TimeSpan delay)
    {
        var cancellation = new CancellationTokenSource();

        if (_processes.TryGetValue(appId, out var process))
        {
            process.SetRestartDelayCancellation(cancellation);
        }

        _ = Task.Run
        (
            async () =>
            {
                try
                {
                    await Task.Delay(delay, cancellation.Token);

                    await _lock.WaitAsync(cancellation.Token);
                    try
                    {
                        if (_processes.TryGetValue(appId, out var p) && p.IsRestarting)
                        {
                            await StartAppInternalAsync(appId, cancellation.Token);
                        }
                    }
                    finally
                    {
                        _lock.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    // Restart was cancelled (app was manually stopped)
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart app {AppId}", appId);
                }
            },
            cancellation.Token
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

    private sealed record AutoStartApp(Guid Id, string ExternalId, string Name, Guid AppTypeId);

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
