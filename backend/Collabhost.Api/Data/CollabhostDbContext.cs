using Collabhost.Api.Domain.Entities;
using Collabhost.Api.Domain.Lookups;

namespace Collabhost.Api.Data;

public class CollabhostDbContext(DbContextOptions<CollabhostDbContext> options) : DbContext(options)
{
    public DbSet<App> Apps => Set<App>();
    public DbSet<EnvironmentVariable> EnvironmentVariables => Set<EnvironmentVariable>();
    public DbSet<AppType> AppTypes => Set<AppType>();
    public DbSet<RestartPolicy> RestartPolicies => Set<RestartPolicy>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollabhostDbContext).Assembly);
    }
}
