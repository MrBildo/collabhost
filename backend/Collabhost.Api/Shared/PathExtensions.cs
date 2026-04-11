namespace Collabhost.Api.Shared;

internal static class PathExtensions
{
    extension(string path)
    {
        public bool IsValidPath()
        {
            var invalidChars = Path.GetInvalidPathChars();

            return !path.AsSpan().ContainsAny(invalidChars);
        }
    }
}
