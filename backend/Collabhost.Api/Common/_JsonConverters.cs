using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Collabhost.Api.Common;

/// <summary>
/// Ensures all DateTime values are serialized with the 'Z' UTC suffix.
/// Raw SQL queries (SqlQuery) bypass EF Core value converters, so DateTime
/// values from SQLite arrive with DateTimeKind.Unspecified. This converter
/// normalizes them at the JSON serialization layer.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dt = reader.GetDateTime();
        return dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK", CultureInfo.InvariantCulture));
    }
}
