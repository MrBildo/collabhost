namespace Collabhost.Api.ActivityLog;

public static class ActivityActor
{
    public const string SystemId = "system";

    public const string SystemName = "System";
}

public static class ActivityEventTypes
{
    public const string AppStarted = "app.started";

    public const string AppStopped = "app.stopped";

    public const string AppRestarted = "app.restarted";

    public const string AppKilled = "app.killed";

    public const string AppCreated = "app.created";

    public const string AppDeleted = "app.deleted";

    public const string AppSettingsUpdated = "app.settings_updated";

    public const string AppCrashed = "app.crashed";

    public const string AppFatal = "app.fatal";

    public const string AppAutoStarted = "app.auto_started";

    public const string AppAutoRestarted = "app.auto_restarted";

    public const string AppSeeded = "app.seeded";

    public const string ProxyReloaded = "proxy.reloaded";

    public const string UserCreated = "user.created";

    public const string UserDeactivated = "user.deactivated";

    public const string UserSeeded = "user.seeded";
}
