namespace Collabhost.Api.Dashboard;

#pragma warning disable MA0053 // API contract records are unsealed by convention -- no inheritance concern for DTOs
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
