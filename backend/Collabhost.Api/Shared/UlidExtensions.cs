using System.Globalization;

namespace Collabhost.Api.Shared;

public static class UlidExtensions
{
    extension(Ulid id)
    {
        // The canonical, culture-invariant string form of a ULID. ULID text is identical across
        // cultures, but the analyzers require an explicit format provider on each conversion. This
        // centralizes that single call so call sites read as intent rather than ceremony. Card 109.
        public string ToCanonicalString() => id.ToString(null, CultureInfo.InvariantCulture);
    }
}
