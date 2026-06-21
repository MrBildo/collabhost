using System.Globalization;

namespace Collabhost.Api.ActivityLog;

// Retention policy for the ActivityEvents table (SVC-01). The store is insert-only -- crash-loops
// add ~3k rows/day/app indefinitely, and every insert maintains 5 b-trees, so unbounded growth is a
// real disk + query-latency cost on a long-lived homelab box. The sweep bounds the table on TWO
// axes, deleting a row that violates EITHER:
//   - MaxAgeDays: rows older than the window are dropped (keeps the table temporally bounded).
//   - MaxCount: only the newest N rows are kept (bounds a crash-loop burst that would otherwise
//     blow past the table size within the age window).
// Either axis is disabled by setting it <= 0 (the same escape-hatch shape as crash-log retention's
// `retention <= 0` guard) -- an operator can run age-only, count-only, or turn the sweep off
// entirely (both <= 0).
//
// The knobs live under the existing `Diagnostics:` namespace alongside `Diagnostics:CrashLogs:*`.
// Resolved off IConfiguration (mirroring CrashLog.ResolveRetention) rather than bound via
// AddSetting<T>, because the values are read once at registration to construct the hosted sweep.
public class ActivityEventRetentionSettings
{
    public const string SectionName = "Diagnostics:ActivityEvents";

    public const string MaxCountKey = "Diagnostics:ActivityEvents:MaxCount";
    public const string MaxAgeDaysKey = "Diagnostics:ActivityEvents:MaxAgeDays";
    public const string SweepIntervalMinutesKey = "Diagnostics:ActivityEvents:SweepIntervalMinutes";

    // Defaults sized for a homelab box: 50k rows is generous for normal operation yet bounds a
    // crash-loop burst; 90 days keeps a season of history; an hourly sweep is far cheaper than the
    // growth it prevents and never touches the insert hot path.
    public const int DefaultMaxCount = 50_000;
    public const int DefaultMaxAgeDays = 90;
    public const int DefaultSweepIntervalMinutes = 60;

    // <= 0 disables the axis (kept verbatim; the resolver only floors the *defaulted* path).
    public required int MaxCount { get; init; }

    public required int MaxAgeDays { get; init; }

    // The sweep cadence always has a positive floor -- a zero/negative interval is meaningless for a
    // PeriodicTimer, so it falls back to the default rather than disabling the sweep. Disable the
    // sweep by zeroing BOTH retention axes, not the cadence.
    public required int SweepIntervalMinutes { get; init; }

    public static ActivityEventRetentionSettings Resolve(IConfiguration? configuration) =>
        new()
        {
            MaxCount = ResolveInt(configuration, MaxCountKey, DefaultMaxCount, floorToDefaultWhenInvalid: false),
            MaxAgeDays = ResolveInt(configuration, MaxAgeDaysKey, DefaultMaxAgeDays, floorToDefaultWhenInvalid: false),
            SweepIntervalMinutes = ResolveInt(configuration, SweepIntervalMinutesKey, DefaultSweepIntervalMinutes, floorToDefaultWhenInvalid: true)
        };

    // A present-and-parseable value is taken verbatim so that a configured retention axis of zero
    // genuinely disables it; a missing or unparseable value falls back to the default. The cadence
    // axis additionally floors a non-positive configured value back to the default, since a timer
    // needs a positive period.
    private static int ResolveInt
    (
        IConfiguration? configuration,
        string key,
        int defaultValue,
        bool floorToDefaultWhenInvalid
    )
    {
        var raw = configuration?[key];

        return string.IsNullOrWhiteSpace(raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? defaultValue
            : floorToDefaultWhenInvalid && parsed <= 0 ? defaultValue : parsed;
    }
}
