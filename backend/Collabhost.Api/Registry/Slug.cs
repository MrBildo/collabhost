namespace Collabhost.Api.Registry;

public readonly partial record struct Slug
{
    public string Value { get; }

    private Slug(string value) => Value = value;

    public static Slug Create(string input)
    {
        var (isValid, error) = Validate(input);
        return !isValid
            ? throw new ArgumentException(error, nameof(input))
            : new Slug(input.Trim().ToLowerInvariant());
    }

    public static (bool IsValid, string? Error) Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (false, "Slug is required.");
        }

        if (input.Length > 63)
        {
            return (false, "Slug cannot exceed 63 characters.");
        }

        if (!SlugPattern.IsMatch(input))
        {
            return (false, "Slug must be lowercase alphanumeric with hyphens, starting and ending with an alphanumeric character.");
        }

        return (true, null);
    }

    public static implicit operator string(Slug slug) => slug.Value;

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern { get; }
}
