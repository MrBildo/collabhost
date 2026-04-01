namespace Collabhost.Api.Features.Filesystem;

public static class Browse
{
    public record DirectoryEntry(string Name, string Path);

    public record Response(string? CurrentPath, string? Parent, IReadOnlyList<DirectoryEntry> Entries);

    public static async Task<Results<Ok<Response>, ProblemHttpResult>> HandleAsync
    (
        [AsParameters] BrowseQuery query,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var command = new BrowseCommand(query.Path);
        var result = await dispatcher.DispatchAsync(command, ct);

        return result.IsSuccess
            ? TypedResults.Ok(result.Value!)
            : TypedResults.Problem(result.ErrorMessage, statusCode: result.ErrorCode == "NOT_FOUND" ? 404 : 400);
    }
}

public record BrowseQuery(string? Path);

public record BrowseCommand(string? Path) : ICommand<Browse.Response>;

public sealed class BrowseCommandHandler : ICommandHandler<BrowseCommand, Browse.Response>
{
    public Task<CommandResult<Browse.Response>> HandleAsync(BrowseCommand command, CancellationToken ct = default)
    {
        var path = command.Path;

        // If path is empty or null, return filesystem roots
        if (string.IsNullOrWhiteSpace(path))
        {
            var roots = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new Browse.DirectoryEntry(d.Name.TrimEnd(global::System.IO.Path.DirectorySeparatorChar), d.RootDirectory.FullName))
                .ToList();

            var response = new Browse.Response(null, null, roots);
            return Task.FromResult(CommandResult<Browse.Response>.Success(response));
        }

        // Security: reject relative path segments
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return Task.FromResult
            (
                CommandResult<Browse.Response>.Fail("INVALID_PATH", "Relative path segments ('..') are not allowed.")
            );
        }

        // Normalize the path
        string normalizedPath;
        try
        {
            normalizedPath = global::System.IO.Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return Task.FromResult
            (
                CommandResult<Browse.Response>.Fail("INVALID_PATH", $"The path '{path}' is not a valid filesystem path.")
            );
        }

        if (!Directory.Exists(normalizedPath))
        {
            return Task.FromResult
            (
                CommandResult<Browse.Response>.Fail("NOT_FOUND", $"The directory '{normalizedPath}' does not exist.")
            );
        }

        var parentPath = Directory.GetParent(normalizedPath)?.FullName;

        List<Browse.DirectoryEntry> entries;
        try
        {
            entries =
            [
                .. Directory.GetDirectories(normalizedPath)
                    .Select(d => new DirectoryInfo(d))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new Browse.DirectoryEntry(d.Name, d.FullName))
            ];
        }
        catch (UnauthorizedAccessException)
        {
            entries = [];
        }

        var browseResponse = new Browse.Response(normalizedPath, parentPath, entries);
        return Task.FromResult(CommandResult<Browse.Response>.Success(browseResponse));
    }
}
