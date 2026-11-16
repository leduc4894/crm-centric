using Npgsql;

namespace Platform.Infrastructure.Identity;

public static class VerifiedTenantTransaction
{
    public static async Task<NpgsqlTransaction> BeginAsync(NpgsqlConnection connection, string issuer, string subject, Guid tenantId, CancellationToken ct)
    {
        var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using var check = new NpgsqlCommand("SELECT platform.lock_active_membership(@issuer,@subject,@tenant)", connection, transaction);
            check.Parameters.AddWithValue("issuer", issuer);
            check.Parameters.AddWithValue("subject", subject);
            check.Parameters.AddWithValue("tenant", tenantId);
            if (await check.ExecuteScalarAsync(ct) is not true)
                throw new UnauthorizedAccessException("Active membership required.");
            await using var setting = new NpgsqlCommand("SELECT set_config('crm.tenant_id', @tenant, true)", connection, transaction);
            setting.Parameters.AddWithValue("tenant", tenantId.ToString());
            await setting.ExecuteNonQueryAsync(ct);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
