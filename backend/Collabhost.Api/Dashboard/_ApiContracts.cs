namespace Collabhost.Api.Dashboard;

public record DashboardStats
(
    int TotalApps,
    int Running,
    int Stopped,
    int Crashed,
    int Issues,
    string? IssuesSummary,
    double? UptimePercent24h,
    int IncidentsThisWeek,
    double? MemoryUsedMb,
    double? MemoryTotalMb,
    double? RequestsPerMinute,
    int AppTypes
);
