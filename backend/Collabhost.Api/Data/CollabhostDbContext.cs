namespace Collabhost.Api.Data;

public class CollabhostDbContext(DbContextOptions<CollabhostDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CollabhostDbContext).Assembly);
    }
}
