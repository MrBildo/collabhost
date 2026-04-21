namespace Collabhost.Api.Platform;

// Seam around BootVersionTracker.Write so integration-test fixtures can substitute a no-op.
// Program.cs resolves this from DI and wires it onto ApplicationStarted; in
// WebApplicationFactory<Program> runs that would otherwise write a .last-boot-version sentinel
// into every test's temp data directory (see #156.1 PR #95 MED-2).
public interface IBootVersionWriter
{
    void Write(string dataDirectory, string version);
}

public sealed class BootVersionWriter(ILogger<BootVersionWriter> logger) : IBootVersionWriter
{
    private readonly ILogger<BootVersionWriter> _logger = logger
        ?? throw new ArgumentNullException(nameof(logger));

    public void Write(string dataDirectory, string version) =>
        BootVersionTracker.Write(dataDirectory, version, _logger);
}
