using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public sealed record FieldDefinition(Guid Id, string Key, string DisplayName, string DataType, bool Required, int Version) : IDefinitionRecord;

public static class CustomFieldStore
{
    public static async Task<List<FieldDefinition>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid? after, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id,key,display_name,data_type,required,version FROM crm.custom_field_defs
            WHERE tenant_id=@tenant AND (@after IS NULL OR id>@after) ORDER BY id LIMIT @limit
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<FieldDefinition>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), reader.GetInt32(5)));
        return rows;
    }

    public static async Task<Guid?> CreateDefinitionAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        string issuer, string subject, string correlation, string kind, string label, string? key, string? type, bool required, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT platform.create_crm_definition($1,$2,$3,$4,$5,$6,$7,$8,$9)", connection, tx);
        foreach (var value in new object[] { tenant, issuer, subject, correlation, kind, label })
            cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)key ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)type ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = required });
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }
}
