using Collabhost.Api.ActivityLog;
using Collabhost.Api.Authorization;
using Collabhost.Api.Capabilities;
using Collabhost.Api.Registry;
using Collabhost.Api.Shared;

namespace Collabhost.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();

    public DbSet<User> Users => Set<User>();

    public DbSet<App> Apps => Set<App>();

    public DbSet<CapabilityOverride> CapabilityOverrides => Set<CapabilityOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<Ulid>().HaveMaxLength(26);
    }
}
