using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Platform.Infrastructure.Persistence;

// Scaffolding is offline: this factory never opens a database.
public sealed class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(provider => provider.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema))
            .Options);
}
