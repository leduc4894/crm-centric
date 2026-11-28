using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public sealed record TimelineEntry(Guid Id, Guid ContactId, Guid? InboxId, string Source, string Type, DateTime OccurredAt, string Summary);

public static class TimelineStore
{
    public static async Task<List<TimelineEntry>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid contact, DateTime? anchorTime, Guid? anchorId, int count, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT id,contact_id,inbox_id,source,type,occurred_at,summary FROM crm.timeline_entries
            WHERE tenant_id=@tenant AND contact_id=@contact AND (@time IS NULL OR (occurred_at,id)<(@time,@id))
            ORDER BY occurred_at DESC,id DESC LIMIT @count
            """, connection, tx);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("contact", contact);
        cmd.Parameters.AddWithValue("time", NpgsqlDbType.TimestampTz, (object?)anchorTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("id", NpgsqlDbType.Uuid, (object?)anchorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("count", count);
        var rows = new List<TimelineEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3), reader.GetString(4), reader.GetDateTime(5), Encoding.UTF8.GetString(reader.GetFieldValue<byte[]>(6))));
        return rows;
    }

    public static async Task<(TimelineEntry Entry, long Version)?> CreateNoteAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid tenant, Guid contact, string issuer, string subject, long expected, string correlation, string text, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT * FROM platform.create_contact_note($1,$2,$3,$4,$5,$6,$7)", connection, tx);
        foreach (var value in new object[] { tenant, contact, issuer, subject, expected, correlation, text.Split('\0') })
            cmd.Parameters.Add(new NpgsqlParameter { Value = value });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (new(reader.GetGuid(0), contact, null, "crm", "crm.note.created.v1", reader.GetDateTime(1), text), reader.GetInt64(2));
    }
}
