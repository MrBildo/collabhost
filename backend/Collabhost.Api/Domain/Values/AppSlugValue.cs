using System.Text.RegularExpressions;

namespace Collabhost.Api.Domain.Values;

public partial class AppSlugValue
{
    // Lowercase alphanumeric and hyphens, no leading/trailing/consecutive hyphens, 1-100 chars
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled)]
    private static partial Regex SlugPattern();

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

        if (!SlugPattern().IsMatch(normalized))
        {
            return (false, "App name must contain only lowercase letters, numbers, and hyphens. It cannot start or end with a hyphen.");
        }

        if (normalized.Contains("--"))
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
