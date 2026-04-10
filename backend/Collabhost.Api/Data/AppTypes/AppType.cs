namespace Collabhost.Api.Data.AppTypes;

public class AppType
{
    public required string Slug { get; init; }

    public required string DisplayName { get; set; }

    public string? Description { get; set; }

    public AppTypeMetadata? Metadata { get; set; }

    public bool IsBuiltIn { get; init; }
}

public class AppTypeMetadata
{
    public RuntimeInfo? Runtime { get; set; }

    public FrameworkInfo? Framework { get; set; }
}

public class RuntimeInfo
{
    public required string Name { get; set; }

    public required string Version { get; set; }

    public string? TargetFramework { get; set; }

    public string? PackageManager { get; set; }
}

public class FrameworkInfo
{
    public required string Name { get; set; }

    public required string Version { get; set; }

    public string? Bundler { get; set; }
}
