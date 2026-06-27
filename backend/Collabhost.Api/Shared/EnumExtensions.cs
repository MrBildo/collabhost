namespace Collabhost.Api.Shared;

public static class EnumExtensions
{
    extension<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        // The declared member name of an enum value (#109). Distinct from the per-enum ToApiString
        // serializers, which map to the wire contract's lowercase/hyphenated strings; this returns
        // the source name verbatim. Enum.GetName is the explicit "I want the name" call; the
        // ToString fallback preserves the numeric-string behavior of an undefined value.
        public string ToName() => Enum.GetName(value) ?? value.ToString();
    }
}
