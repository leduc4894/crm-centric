using Microsoft.EntityFrameworkCore;

namespace Platform.Infrastructure.Persistence;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string HistorySchema = "platform";
    public const string HistoryTable = "__EFMigrationsHistory";

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.HasDefaultSchema(HistorySchema);
}
