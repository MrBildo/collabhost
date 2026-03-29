using System.Text.RegularExpressions;

namespace Collabhost.Api.Domain.Values;

public sealed partial class AppSlugValue
{
    // Lowercase alphanumeric and hyphens, no leading/trailing/consecutive hyphens, 1-100 chars
    // MA0009: Not vulnerable — simple character class [a-z0-9-] with no nested quantifiers, input bounded to 100 chars
#pragma warning disable MA0009
    [GeneratedRegex(@"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.ExplicitCapture)]
#pragma warning restore MA0009
    private static partial Regex SlugPattern { get; }

    public string Value { get; }

    public static (bool IsValid, string? Error) CanCreate(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return (false, "App name is required.");
        }

        var normalized = slug.Trim().ToLowerInvariant();

        if (normalized.Length > 100)
        {
            return (false, "App name must not exceed 100 characters.");
        }

        if (!SlugPattern.IsMatch(normalized))
        {
            return (false, "App name must contain only lowercase letters, numbers, and hyphens. It cannot start or end with a hyphen.");
        }

        if (normalized.Contains("--", StringComparison.Ordinal))
        {
            return (false, "App name must not contain consecutive hyphens.");
        }

        return (true, null);
    }

    public static AppSlugValue Create(string slug)
    {
        var (isValid, error) = CanCreate(slug);

        return !isValid ? throw new ArgumentException(error, nameof(slug)) : new AppSlugValue(slug);
    }

    private AppSlugValue(string slug)
    {
        Value = slug.Trim().ToLowerInvariant();
    }

    public static implicit operator string(AppSlugValue slug) => slug.Value;

    public override string ToString() => Value;
}
