using Npgsql;
using Platform.Api.Operations;
using Platform.Infrastructure.Configuration;

namespace Platform.Api.Identity;

internal static class TenantEndpointTransaction
{
    internal static async Task RunAsync(HttpContext context, IConfiguration configuration, bool mutation, Func<NpgsqlConnection, NpgsqlTransaction, Guid, Func<Task>, Task> operation,
        TenantPermission permission = TenantPermission.TeamManage)
    {
        for (var attempt = 0; ; attempt++)
        {
            var commitStarted = false;
            try
            {
                await using var connection = new NpgsqlConnection(DatabaseOptions.Parse(configuration.GetConnectionString("Platform")).ConnectionString);
                await connection.OpenAsync(context.RequestAborted);
                var auth = await TenantAuthorization.BeginAsync(context, connection, mutation, context.RequestAborted, permission);
                if (auth.Transaction is null)
                {
                    await OperationalErrors.WriteProblemAsync(context, auth.ReasonToken == "TENANT_CONTEXT_INVALID" ? 400 : 403, auth.ReasonToken!, context.RequestAborted);
                    return;
                }
                await using var transaction = auth.Transaction;
                await operation(connection, transaction, auth.TenantId, async () => { commitStarted = true; await transaction.CommitAsync(context.RequestAborted); });
                return;
            }
            catch (PostgresException ex) when (ex.SqlState is "40P01" or "40001" && attempt < 2 && !commitStarted && !context.Response.HasStarted)
            {
                await Task.Delay(25 * (attempt + 1), context.RequestAborted);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidDataException)
            {
                if (!context.Response.HasStarted) await OperationalErrors.WriteProblemAsync(context, 503, "IDENTITY_STORE_UNAVAILABLE", context.RequestAborted);
                return;
            }
        }
    }
}
