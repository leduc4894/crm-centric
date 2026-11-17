using Npgsql;

namespace Platform.Infrastructure.Identity;

public static class MembershipMutationTransaction
{
    public static async Task<NpgsqlTransaction> BeginAsync(NpgsqlConnection connection, string issuer, string subject, Guid tenantId, CancellationToken ct)
    {
        var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using var advisoryLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('crm.membership:' || @tenant::uuid::text, 0))", connection, transaction);
            advisoryLock.Parameters.AddWithValue("tenant", tenantId);
            await advisoryLock.ExecuteNonQueryAsync(ct);
            await VerifiedTenantTransaction.VerifyAsync(connection, transaction, issuer, subject, tenantId, ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
