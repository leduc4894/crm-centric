using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Platform.Infrastructure.Persistence;

public enum DatabaseReadinessResult { Ready, Unavailable, SchemaNotReady }

public class DatabaseReadiness(PlatformDbContext database)
{
    public virtual async Task<DatabaseReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await database.Database.ExecuteSqlRawAsync("SELECT 1", budget.Token);
            var applied = await database.Database.GetAppliedMigrationsAsync(budget.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return database.Database.GetMigrations().ToHashSet(StringComparer.Ordinal).SetEquals(applied)
                ? DatabaseReadinessResult.Ready : DatabaseReadinessResult.SchemaNotReady;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return DatabaseReadinessResult.Unavailable;
        }
        catch (NpgsqlException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.InvalidSchemaName }
                ? DatabaseReadinessResult.SchemaNotReady : DatabaseReadinessResult.Unavailable;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DatabaseReadinessResult.Unavailable;
        }
    }
}
