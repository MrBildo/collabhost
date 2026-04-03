using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Collabhost.Api.Shared;

/// <summary>
/// EF Core value converter that normalizes DateTime values to UTC on read.
/// SQLite stores DateTime as text — this ensures Kind is always Utc.
/// </summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
