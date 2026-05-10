namespace Collabhost.Api.Dashboard;

public record DashboardStats
(
    int TotalApps,
    int Running,
    int Stopped,
    int Crashed,
    int Backoff,
    int Fatal,
    int Issues,
    string? IssuesSummary,
    int AppTypes
);

public record DashboardEventResponse
(
    string Id,
    DateTime Timestamp,
    string Message,
    string? AppSlug,
    string Source,
    string Severity
);
