using System.Globalization;

using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<App> Apps => Set<App>();

    public DbSet<AppType> AppTypes => Set<AppType>();

    public DbSet<CapabilityBinding> CapabilityBindings => Set<CapabilityBinding>();

    public DbSet<CapabilityOverride> CapabilityOverrides => Set<CapabilityOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureApp(modelBuilder);
        ConfigureAppType(modelBuilder);
        ConfigureCapabilityBinding(modelBuilder);
        ConfigureCapabilityOverride(modelBuilder);
        SeedData(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<Ulid>().HaveMaxLength(26);
    }

    private static void ConfigureApp(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<App>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.Id)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(a => a.Slug)
                .HasMaxLength(100); // slug max length

            entity.HasIndex(a => a.Slug)
                .IsUnique();

            entity.Property(a => a.DisplayName)
                .HasMaxLength(200); // display name max length

            entity.Property(a => a.AppTypeId)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.HasOne(a => a.AppType)
                .WithMany()
                .HasForeignKey(a => a.AppTypeId);
        });

    private static void ConfigureAppType(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<AppType>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.Id)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(t => t.Slug)
                .HasMaxLength(100); // slug max length

            entity.HasIndex(t => t.Slug)
                .IsUnique();

            entity.Property(t => t.DisplayName)
                .HasMaxLength(200); // display name max length

            entity.Property(t => t.Description)
                .HasMaxLength(500); // description max length

            entity.HasMany(t => t.Bindings)
                .WithOne()
                .HasForeignKey(b => b.AppTypeId);
        });

    private static void ConfigureCapabilityBinding(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CapabilityBinding>(entity =>
        {
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Id)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(b => b.AppTypeId)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(b => b.CapabilitySlug)
                .HasMaxLength(50); // capability slug max length

            entity.HasIndex(b => new { b.AppTypeId, b.CapabilitySlug })
                .IsUnique();
        });

    private static void ConfigureCapabilityOverride(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CapabilityOverride>(entity =>
        {
            entity.HasKey(o => o.Id);

            entity.Property(o => o.Id)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(o => o.AppId)
                .HasConversion(v => v.ToString(null, CultureInfo.InvariantCulture), v => Ulid.Parse(v, CultureInfo.InvariantCulture))
                .HasMaxLength(26); // ULID string representation

            entity.Property(o => o.CapabilitySlug)
                .HasMaxLength(50); // capability slug max length

            entity.HasIndex(o => new { o.AppId, o.CapabilitySlug })
                .IsUnique();

            entity.HasOne<App>()
                .WithMany()
                .HasForeignKey(o => o.AppId);
        });

#pragma warning disable CA1305, MA0011, MA0076 // Ulid.Parse/ToString are not locale-sensitive
    private static void SeedData(ModelBuilder modelBuilder)
    {
        var dotnetApp = SeedAppTypes(modelBuilder);
        SeedDotNetBindings(modelBuilder, dotnetApp.DotNet);
        SeedNodeJsBindings(modelBuilder, dotnetApp.NodeJs);
        SeedStaticSiteBindings(modelBuilder, dotnetApp.StaticSite);
    }

    private static (AppType DotNet, AppType NodeJs, AppType StaticSite) SeedAppTypes(ModelBuilder modelBuilder)
    {
        var dotnetApp = new AppType
        {
            Id = Ulid.Parse("01JQDZ000000000000000DNETA"),
            Slug = "dotnet-app",
            DisplayName = ".NET Application",
            Description = "ASP.NET Core or .NET console application",
            IsBuiltIn = true,
            MetadataJson = """{"runtime":{"name":".NET","version":"10","targetFramework":"net10.0"}}"""
        };

        var nodejsApp = new AppType
        {
            Id = Ulid.Parse("01JQDZ000000000000000NODEA"),
            Slug = "nodejs-app",
            DisplayName = "Node.js Application",
            Description = "Server-side JavaScript with npm",
            IsBuiltIn = true,
            MetadataJson = """{"runtime":{"name":"Node.js","version":"22","packageManager":"npm"}}"""
        };

        var staticSite = new AppType
        {
            Id = Ulid.Parse("01JQDZ000000000000000STATA"),
            Slug = "static-site",
            DisplayName = "Static Site",
            Description = "Static files served by Caddy",
            IsBuiltIn = true,
            MetadataJson = null
        };

        modelBuilder.Entity<AppType>().HasData(dotnetApp, nodejsApp, staticSite);

        return (dotnetApp, nodejsApp, staticSite);
    }

    private static void SeedDotNetBindings(ModelBuilder modelBuilder, AppType dotnetApp) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND01"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND02"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"DotNetRuntimeConfiguration","gracefulShutdown":true,"shutdownTimeoutSeconds":30}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND03"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "port-injection",
                DefaultConfigurationJson = """{"environmentVariableName":"ASPNETCORE_URLS","portFormat":"http://localhost:{port}"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND04"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"ReverseProxy","spaFallback":false}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND05"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "health-check",
                DefaultConfigurationJson = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND06"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND07"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":true}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000DNETBND08"),
                AppTypeId = dotnetApp.Id,
                CapabilitySlug = "environment-defaults",
                DefaultConfigurationJson = """{"variables":{"ASPNETCORE_ENVIRONMENT":"Production"}}"""
            }
        );

    private static void SeedNodeJsBindings(ModelBuilder modelBuilder, AppType nodejsApp) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND01"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND02"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "process",
                DefaultConfigurationJson = """{"discoveryStrategy":"PackageJson","gracefulShutdown":true,"shutdownTimeoutSeconds":15}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND03"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "port-injection",
                DefaultConfigurationJson = """{"environmentVariableName":"PORT","portFormat":"{port}"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND04"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"ReverseProxy","spaFallback":false}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND05"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "health-check",
                DefaultConfigurationJson = """{"endpoint":"/health","intervalSeconds":30,"timeoutSeconds":5}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND06"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "restart",
                DefaultConfigurationJson = """{"policy":"OnCrash"}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND07"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "auto-start",
                DefaultConfigurationJson = """{"enabled":true}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000NODEBND08"),
                AppTypeId = nodejsApp.Id,
                CapabilitySlug = "environment-defaults",
                DefaultConfigurationJson = """{"variables":{"NODE_ENV":"production"}}"""
            }
        );

    private static void SeedStaticSiteBindings(ModelBuilder modelBuilder, AppType staticSite) =>
        modelBuilder.Entity<CapabilityBinding>().HasData
        (
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000STATBND01"),
                AppTypeId = staticSite.Id,
                CapabilitySlug = "artifact",
                DefaultConfigurationJson = """{"location":""}"""
            },
            new CapabilityBinding
            {
                Id = Ulid.Parse("01JQDZ00000000000STATBND02"),
                AppTypeId = staticSite.Id,
                CapabilitySlug = "routing",
                DefaultConfigurationJson = """{"domainPattern":"{slug}.collab.internal","serveMode":"FileServer","spaFallback":true}"""
            }
        );
#pragma warning restore CA1305, MA0011, MA0076
}
