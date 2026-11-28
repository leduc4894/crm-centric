using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public interface IDefinitionRecord { Guid Id { get; } }
public sealed record TagRecord(Guid Id, string Name) : IDefinitionRecord;

public static class TagStore
{
    public static Task<List<TagRecord>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid? after, int limit, CancellationToken ct) =>
        ListAsync(connection, tx, tenant, null, after, limit, ct);

    public static async Task<List<TagRecord>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid? contact, Guid? after, int limit, CancellationToken ct)
    {
        var sql = contact.HasValue ? """
            SELECT t.id,t.name,t.name_parts FROM crm.contact_tags ct
            JOIN crm.tags t ON t.tenant_id=ct.tenant_id AND t.id=ct.tag_id
            WHERE ct.tenant_id=@tenant AND ct.contact_id=@contact AND (@after IS NULL OR ct.tag_id>@after)
            ORDER BY ct.tag_id LIMIT @limit
            """ : """
            SELECT id,name,name_parts FROM crm.tags WHERE tenant_id=@tenant AND (@after IS NULL OR id>@after)
            ORDER BY id LIMIT @limit
            """;
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        if (contact.HasValue) command.Parameters.AddWithValue("contact", contact.Value);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<TagRecord>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.IsDBNull(2) ? reader.GetString(1) : string.Join('\0', reader.GetFieldValue<string[]>(2))));
        return rows;
    }

}
