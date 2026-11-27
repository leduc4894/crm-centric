using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Identity;

public sealed record AuditEntry(Guid Id, string Actor, string Action, Guid Target, DateTime Timestamp, string CorrelationId, string Summary);

public static class AuditStore
{
    public static async Task<List<AuditEntry>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenant, string issuer, string subject, DateTime? timestamp, Guid? id, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT * FROM platform.list_audit_entries(@tenant,@issuer,@subject,@timestamp,@id,@limit)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("timestamp", NpgsqlDbType.TimestampTz, (object?)timestamp ?? DBNull.Value);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)id ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit + 1);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<AuditEntry>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.GetDateTime(4), reader.GetString(5), reader.GetString(6)));
        return items;
    }
}
