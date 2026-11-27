using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Identity;

public sealed record TenantMembership(Guid Id, string Name, string Status);

public static class MembershipLookup
{
    public static async Task<IReadOnlyList<TenantMembership>> ListAsync(NpgsqlConnection connection, string issuer, string subject, Guid? afterTenantId, int take, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id, name, status FROM platform.list_principal_tenants(@issuer,@subject,@after,@take)", connection);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)afterTenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("take", take);
        var results = new List<TenantMembership>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        return results;
    }
}
