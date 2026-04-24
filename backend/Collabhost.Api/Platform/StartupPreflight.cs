namespace Collabhost.Api.Platform;

public static class StartupPreflight
{
    public const string BackupsSubdirectory = "backups";

    public static PreflightResult Validate
    (
        string dataDirectory,
        string? userTypesDirectory = null,
        ILogger? logger = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // (1) Data directory exists and is writable. Create if missing.
        if (!TryEnsureDirectory(dataDirectory, out var dataDirError))
        {
            return PreflightResult.Fail
            (
                summary: "data directory not writable",
                details: [("Path", dataDirectory), ("Error", dataDirError ?? "unknown")],
                recoverySteps:
                [
                    "Verify filesystem permissions on the configured data directory.",
                    "Ensure the Collabhost process user owns or has write access to this path.",
                    "Override the location with the COLLABHOST_DATA_PATH environment variable if needed."
                ]
            );
        }

        if (!TryWriteSentinel(dataDirectory, out var sentinelError))
        {
            return PreflightResult.Fail
            (
                summary: "data directory not writable",
                details: [("Path", dataDirectory), ("Error", sentinelError ?? "unknown")],
                recoverySteps:
                [
                    "Verify filesystem permissions on the configured data directory.",
                    "Ensure the Collabhost process user owns or has write access to this path."
                ]
            );
        }

        // (2) Backups directory exists and is writable.
        var backupsDirectory = Path.Combine(dataDirectory, BackupsSubdirectory);

        if (!TryEnsureDirectory(backupsDirectory, out var backupsError))
        {
            return PreflightResult.Fail
            (
                summary: "backups directory not writable",
                details: [("Path", backupsDirectory), ("Error", backupsError ?? "unknown")],
                recoverySteps:
                [
                    "Verify filesystem permissions on the data directory.",
                    "Ensure the Collabhost process user can create the 'backups' subdirectory."
                ]
            );
        }

        // (3) AppContext.BaseDirectory is readable. Packaging sanity check.
        var baseDir = AppContext.BaseDirectory;

        if (!Directory.Exists(baseDir))
        {
            return PreflightResult.Fail
            (
                summary: "base directory not accessible",
                details: [("Path", baseDir)],
                recoverySteps: ["This indicates a corrupt installation; reinstall Collabhost."]
            );
        }

        // (4) User-types directory. Created if missing so operators can drop *.json files
        // into it without a restart-then-discover loop. TypeStore.StartWatching picks up
        // new files via FileSystemWatcher once the host is running.
        var userTypesCreated = false;

        if (!string.IsNullOrWhiteSpace(userTypesDirectory))
        {
            var existed = Directory.Exists(userTypesDirectory);

            if (!TryEnsureDirectory(userTypesDirectory, out var userTypesError))
            {
                return PreflightResult.Fail
                (
                    summary: "user-types directory not writable",
                    details: [("Path", userTypesDirectory), ("Error", userTypesError ?? "unknown")],
                    recoverySteps:
                    [
                        "Verify filesystem permissions on the configured user-types directory.",
                        "Override the location with the COLLABHOST_USER_TYPES_PATH environment variable if needed."
                    ]
                );
            }

            userTypesCreated = !existed;

            if (userTypesCreated)
            {
                logger?.LogInformation
                (
                    "User-types directory initialized at {Directory} -- drop *.json files here to register custom app types.",
                    userTypesDirectory
                );
            }
        }

        logger?.LogInformation
        (
            "Startup preflight ok: data={DataDirectory} backups={BackupsDirectory} userTypes={UserTypesDirectory}",
            dataDirectory,
            backupsDirectory,
            userTypesDirectory ?? "(not resolved)"
        );

        return PreflightResult.Ok(dataDirectory, backupsDirectory, userTypesDirectory, userTypesCreated);
    }

    private static bool TryEnsureDirectory(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(path);
            error = null;
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (ArgumentException ex)
        {
            // Malformed path (e.g. COLLABHOST_DATA_PATH / COLLABHOST_USER_TYPES_PATH set to
            // a string with invalid path characters). Operator configuration error -- exit 10.
            error = ex.Message;
            return false;
        }
        catch (NotSupportedException ex)
        {
            // Path contains a colon in a non-drive position on Windows (e.g. "?invalid?").
            // Operator configuration error -- exit 10.
            error = ex.Message;
            return false;
        }
    }

    private static bool TryWriteSentinel(string directory, out string? error)
    {
        var sentinel = Path.Combine(directory, ".preflight-sentinel");

        try
        {
            File.WriteAllText(sentinel, string.Empty);
            File.Delete(sentinel);
            error = null;
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

public sealed record PreflightResult
{
    private PreflightResult
    (
        bool success,
        string? dataDirectory,
        string? backupsDirectory,
        string? userTypesDirectory,
        bool userTypesDirectoryCreated,
        string? failureSummary,
        IReadOnlyList<(string Label, string Value)>? failureDetails,
        IReadOnlyList<string>? recoverySteps
    )
    {
        Success = success;
        DataDirectory = dataDirectory;
        BackupsDirectory = backupsDirectory;
        UserTypesDirectory = userTypesDirectory;
        UserTypesDirectoryCreated = userTypesDirectoryCreated;
        FailureSummary = failureSummary;
        FailureDetails = failureDetails ?? [];
        RecoverySteps = recoverySteps ?? [];
    }

    public bool Success { get; }

    public string? DataDirectory { get; }

    public string? BackupsDirectory { get; }

    public string? UserTypesDirectory { get; }

    public bool UserTypesDirectoryCreated { get; }

    public string? FailureSummary { get; }

    public IReadOnlyList<(string Label, string Value)> FailureDetails { get; }

    public IReadOnlyList<string> RecoverySteps { get; }

    public static PreflightResult Ok
    (
        string dataDirectory,
        string backupsDirectory,
        string? userTypesDirectory = null,
        bool userTypesDirectoryCreated = false
    ) =>
        new
        (
            success: true,
            dataDirectory: dataDirectory,
            backupsDirectory: backupsDirectory,
            userTypesDirectory: userTypesDirectory,
            userTypesDirectoryCreated: userTypesDirectoryCreated,
            failureSummary: null,
            failureDetails: null,
            recoverySteps: null
        );

    public static PreflightResult Fail
    (
        string summary,
        IReadOnlyList<(string Label, string Value)> details,
        IReadOnlyList<string> recoverySteps
    ) =>
        new
        (
            success: false,
            dataDirectory: null,
            backupsDirectory: null,
            userTypesDirectory: null,
            userTypesDirectoryCreated: false,
            failureSummary: summary,
            failureDetails: details,
            recoverySteps: recoverySteps
        );
}
