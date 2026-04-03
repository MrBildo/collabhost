using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Collabhost.Api.Shared;

// SQLite stores DateTime as text without Kind — normalize to Utc on read
public class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>
    (
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    );
