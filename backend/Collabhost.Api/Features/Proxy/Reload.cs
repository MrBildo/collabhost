using Collabhost.Api.Services;

namespace Collabhost.Api.Features.Proxy;

public static class Reload
{
    public static async Task<Results<Ok<ReloadResponse>, ProblemHttpResult>> HandleAsync
    (
        ProxyConfigManager proxyConfigManager
    )
    {
        try
        {
            await proxyConfigManager.SyncRoutesAsync();
            return TypedResults.Ok(new ReloadResponse(true, "Proxy config reloaded successfully."));
        }
        catch (Exception)
        {
            return TypedResults.Problem("Proxy config reload failed.", statusCode: 500);
        }
    }

    public record ReloadResponse(bool Success, string Message);
}
