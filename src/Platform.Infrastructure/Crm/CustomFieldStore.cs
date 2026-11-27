using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public sealed record FieldDefinition(Guid Id, string Key, string DisplayName, string DataType, bool Required, int Version) : IDefinitionRecord;

public enum AttributeChange { Conflict, Missing, Invalid, Unrepresentable, Unchanged, Changed }

public static class CustomFieldStore
{
    public static async Task<string?> GetValueAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid contact, Guid field, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT CASE WHEN v.escaped_string THEN v.value #>> '{}' ELSE v.value::text END
            FROM crm.contact_field_values v
            JOIN crm.contacts c ON (c.tenant_id,c.id)=(v.tenant_id,v.contact_id)
            WHERE v.tenant_id=@tenant AND v.contact_id=@contact AND v.field_id=@field AND c.deleted_at IS NULL
            """, connection, tx);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("contact", contact);
        cmd.Parameters.AddWithValue("field", field);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public static async Task<List<FieldDefinition>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid? after, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id,key,display_name,data_type,required,version,display_parts FROM crm.custom_field_defs
            WHERE tenant_id=@tenant AND (@after IS NULL OR id>@after) ORDER BY id LIMIT @limit
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<FieldDefinition>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(6) ? reader.GetString(2) : string.Join('\0', reader.GetFieldValue<string[]>(6)), reader.GetString(3), reader.GetBoolean(4), reader.GetInt32(5)));
        return rows;
    }

    public static async Task<Guid?> CreateDefinitionAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        string issuer, string subject, string correlation, string kind, string label, string? key, string? type, bool required, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT platform.create_crm_definition($1,$2,$3,$4,$5,$6,$7,$8,$9)", connection, tx);
        foreach (var value in new object[] { tenant, issuer, subject, correlation, kind })
            cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = label.Split('\0') });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)key ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)type ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter { Value = required });
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }
}
