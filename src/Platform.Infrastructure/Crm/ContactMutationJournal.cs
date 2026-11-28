using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public static class ContactMutationJournal
{
    // The caller owns and commits the transaction; failures must propagate.
    public static async Task<Guid> AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid contactId, string issuer, string subject, string action, string correlationId,
        long version, CancellationToken ct)
    {
        var eventType = action switch
        {
            "contact.created" => "crm.contact.created.v1",
            "contact.updated" => "crm.contact.updated.v1",
            "contact.deleted" => "crm.contact.deleted.v1",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        var eventId = Guid.NewGuid();
        var occurredAt = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            eventId, tenantId, contactId, eventType, occurredAt, payload = new { version }
        });
        await using var command = new NpgsqlCommand("""
            SELECT platform.append_contact_audit(@tenant,@contact,@issuer,@subject,@action,@correlation);
            INSERT INTO outbox.events(tenant_id,id,contact_id,type,occurred_at,payload)
              VALUES(@tenant,@event,@contact,@type,@occurred,@payload);
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("contact", contactId);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("correlation", correlationId);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("type", eventType);
        command.Parameters.AddWithValue("occurred", occurredAt);
        command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload);
        await command.ExecuteNonQueryAsync(ct);
        return eventId;
    }
}
