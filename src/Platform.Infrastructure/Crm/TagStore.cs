using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public interface IDefinitionRecord { Guid Id { get; } }
public sealed record TagRecord(Guid Id, string Name) : IDefinitionRecord;

public static class TagStore
{
    public static async Task<List<TagRecord>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid? after, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id,name FROM crm.tags WHERE tenant_id=@tenant AND (@after IS NULL OR id>@after)
            ORDER BY id LIMIT @limit
            """, connection, tx);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)after ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<TagRecord>();
        while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetGuid(0), reader.GetString(1)));
        return rows;
    }
}
