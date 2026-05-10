using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Collabhost.Api.Tests.Fixtures;

// Minimal IHostEnvironment for test instances of services that depend on the host
// environment for path resolution (notably TypeStore, post-#247). Defaults the content
// root to AppContext.BaseDirectory -- matches the env-var-unset posture and keeps the
// behavior identical to the pre-#247 AppContext.BaseDirectory anchor.
internal sealed class StubHostEnvironment : IHostEnvironment
{
    public StubHostEnvironment(string? contentRootPath = null)
    {
        ContentRootPath = contentRootPath ?? AppContext.BaseDirectory;
        ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
    }

    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "Collabhost.Api.Tests";

    public string ContentRootPath { get; set; }

    public IFileProvider ContentRootFileProvider { get; set; }
}
