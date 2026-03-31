using System.Diagnostics;
using System.Text.Json;

using Collabhost.Api.Domain.Catalogs;
using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Values;

namespace Collabhost.Api.Services.Proxy;

public sealed class ProxyAppSeeder
(
    CollabhostDbContext db,
    ProxySettings settings,
    ILogger<ProxyAppSeeder> logger
) : IProxyAppSeeder
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly ProxySettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly ILogger<ProxyAppSeeder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Check if proxy app already exists by name
        var existingCount = await _db.Database
            .SqlQuery<int>
            (
                $"""
                SELECT
                    COUNT(*) AS [Value]
                FROM
                    [App] A
                WHERE
                    A.[Name] = 'proxy'
                """
            )
            .SingleAsync(cancellationToken);

        if (existingCount > 0)
        {
            _logger.LogInformation("Proxy app already registered — skipping seed");
            return;
        }

        var resolvedPath = ResolveBinaryPath(_settings.BinaryPath);

        if (resolvedPath is null)
        {
            _logger.LogWarning
            (
                "Proxy binary not found at '{BinaryPath}'. Proxy features will be disabled.\n" +
                "To install:\n" +
                "  Windows: winget install CaddyServer.Caddy\n" +
                "  Or download to tools/caddy/ and set Proxy:BinaryPath in appsettings.Development.json\n" +
                "  General: https://caddyserver.com/docs/install",
                _settings.BinaryPath
            );
            return;
        }

        // Use Executable type for the proxy app
        var app = App.Register
        (
            AppSlugValue.Create("proxy"),
            "Proxy",
            IdentifierCatalog.AppTypes.Executable
        );

        _db.Apps.Add(app);
        await _db.SaveChangesAsync(cancellationToken);

        await CreateCapabilityOverridesAsync(app.Id, resolvedPath, cancellationToken);

        _logger.LogInformation
        (
            "Proxy app seeded — binary at '{BinaryPath}'",
            resolvedPath
        );
    }

    private async Task CreateCapabilityOverridesAsync
    (
        Guid appId,
        string resolvedPath,
        CancellationToken cancellationToken
    )
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Look up AppTypeCapability rows for the Executable type
        var processTypeCapability = await _db.Set<AppTypeCapability>()
            .AsNoTracking()
            .Where
            (
                atc => atc.AppTypeId == IdentifierCatalog.AppTypes.Executable
                    && atc.CapabilityId == IdentifierCatalog.Capabilities.Process
            )
            .SingleAsync(cancellationToken);

        var autoStartTypeCapability = await _db.Set<AppTypeCapability>()
            .AsNoTracking()
            .Where
            (
                atc => atc.AppTypeId == IdentifierCatalog.AppTypes.Executable
                    && atc.CapabilityId == IdentifierCatalog.Capabilities.AutoStart
            )
            .SingleAsync(cancellationToken);

        // Process capability override
        var processOverride = new
        {
            discoveryStrategy = "manual",
            command = resolvedPath,
            arguments = "run --config \"\"",
            workingDirectory = Path.GetDirectoryName(resolvedPath),
            gracefulShutdown = true,
            shutdownTimeoutSeconds = 10
        };

        var processConfiguration = CapabilityConfiguration.Create
        (
            appId,
            processTypeCapability.Id,
            JsonSerializer.Serialize(processOverride, jsonOptions)
        );

        _db.Set<CapabilityConfiguration>().Add(processConfiguration);

        // Auto-start capability override
        var autoStartOverride = new { enabled = true };

        var autoStartConfiguration = CapabilityConfiguration.Create
        (
            appId,
            autoStartTypeCapability.Id,
            JsonSerializer.Serialize(autoStartOverride, jsonOptions)
        );

        _db.Set<CapabilityConfiguration>().Add(autoStartConfiguration);

        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static string? ResolveBinaryPath(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        // If the path contains a directory separator, treat it as an absolute/relative path
        if (binaryPath.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || binaryPath.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return File.Exists(binaryPath) ? Path.GetFullPath(binaryPath) : null;
        }

        // Bare name — resolve via PATH using where (Windows) or which (Linux)
        return ResolveFromPath(binaryPath);
    }

    private static string? ResolveFromPath(string binaryName)
    {
        var command = OperatingSystem.IsWindows() ? "where" : "which";

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = binaryName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                // 'where' on Windows may return multiple lines — take the first
                var firstLine = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0];
                return firstLine;
            }
        }
        catch (Exception)
        {
            // Binary resolution failed — will be reported as not found
        }

        return null;
    }
}
