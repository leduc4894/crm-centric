using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public sealed record ContactRecord(Guid Id, string DisplayName, string? Email, string? Phone, long Version, DateTime CreatedAt, DateTime UpdatedAt);

public static class ContactStore
{
    public static async Task<List<ContactRecord>> ListAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        string? q, Guid? tagId, DateTimeOffset? updatedSince, DateTime? anchorTime, Guid? anchorId, int limit, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT c.id,c.display_name,c.email,c.phone,c.version,c.created_at,c.updated_at FROM crm.contacts c
            WHERE c.tenant_id=@tenant AND c.deleted_at IS NULL
              AND (@q IS NULL OR c.display_name ILIKE @q ESCAPE E'\\' OR c.email ILIKE @q ESCAPE E'\\')
              AND (@tag IS NULL OR EXISTS (SELECT 1 FROM crm.contact_tags ct JOIN crm.tags t ON t.tenant_id=ct.tenant_id AND t.id=ct.tag_id
                    WHERE ct.tenant_id=c.tenant_id AND ct.contact_id=c.id AND ct.tag_id=@tag))
              AND (@since IS NULL OR c.updated_at >= @since)
              AND (@anchor IS NULL OR (c.updated_at,c.id) < (@anchor,@anchor_id))
            ORDER BY c.updated_at DESC,c.id DESC LIMIT @count
            """,connection,transaction);
        var pattern = q is null ? null : "%" + q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        command.Parameters.AddWithValue("tenant",tenantId);
        command.Parameters.AddWithValue("q",NpgsqlDbType.Text,(object?)pattern ?? DBNull.Value);
        command.Parameters.AddWithValue("tag",NpgsqlDbType.Uuid,(object?)tagId ?? DBNull.Value);
        command.Parameters.AddWithValue("since",NpgsqlDbType.TimestampTz,(object?)updatedSince ?? DBNull.Value);
        command.Parameters.AddWithValue("anchor",NpgsqlDbType.TimestampTz,(object?)anchorTime ?? DBNull.Value);
        command.Parameters.AddWithValue("anchor_id",NpgsqlDbType.Uuid,(object?)anchorId ?? DBNull.Value);
        command.Parameters.AddWithValue("count",limit+1);
        var result=new List<ContactRecord>(); await using var reader=await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public static async Task<ContactRecord> CreateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        string displayName, string? email, string? phone, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO crm.contacts (tenant_id,id,display_name,email,phone)
            VALUES (@tenant,@id,@name,@email,@phone)
            RETURNING id,display_name,email,phone,version,created_at,updated_at
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",Guid.NewGuid());
        command.Parameters.AddWithValue("name",displayName);
        command.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)email ?? DBNull.Value);
        command.Parameters.AddWithValue("phone",NpgsqlDbType.Text,(object?)phone ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    public static async Task<ContactRecord?> GetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id,display_name,email,phone,version,created_at,updated_at FROM crm.contacts
            WHERE tenant_id=@tenant AND id=@id AND deleted_at IS NULL
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    // Serialize POSTs for the same normalized email, including the initial insert.
    // The database computes both lock and lookup keys using its own collation.
    public static async Task<ContactRecord?> LockByEmailAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, string email, CancellationToken ct)
    {
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@tenant::text || ':' || lower(btrim(@email)), 0))", connection, transaction))
        {
            advisory.Parameters.AddWithValue("tenant",tenantId); advisory.Parameters.AddWithValue("email",email);
            await advisory.ExecuteNonQueryAsync(ct);
        }
        await using var command = new NpgsqlCommand("""
            SELECT id,display_name,email,phone,version,created_at,updated_at FROM crm.contacts
            WHERE tenant_id=@tenant AND email_normalized=lower(btrim(@email)) AND deleted_at IS NULL FOR UPDATE
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("email",email);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public static async Task<ContactRecord?> UpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        Guid id, long version, string name, string email, bool phonePresent, string? phone, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE crm.contacts SET display_name=@name,email=@email,phone=CASE WHEN @phone_present THEN @phone ELSE phone END,
              version=version+1,updated_at=now()
            WHERE tenant_id=@tenant AND id=@id AND version=@version AND deleted_at IS NULL
            RETURNING id,display_name,email,phone,version,created_at,updated_at
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",id);
        command.Parameters.AddWithValue("version",version); command.Parameters.AddWithValue("name",name); command.Parameters.AddWithValue("email",email);
        command.Parameters.AddWithValue("phone_present",phonePresent); command.Parameters.AddWithValue("phone",NpgsqlDbType.Text,(object?)phone ?? DBNull.Value);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public sealed record LockedContact(ContactRecord Contact, bool Deleted);

    public static async Task<LockedContact?> LockByIdAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid id, CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
            SELECT id,display_name,email,phone,version,created_at,updated_at,deleted_at FROM crm.contacts
            WHERE tenant_id=@tenant AND id=@id FOR UPDATE
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",id);
        await using var reader=await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new LockedContact(Read(reader),!reader.IsDBNull(7)) : null;
    }

    public static async Task<ContactRecord?> PatchAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid tenantId,Guid id,long version,
        bool namePresent,string? name,bool emailPresent,string? email,bool phonePresent,string? phone,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
            UPDATE crm.contacts SET display_name=CASE WHEN @name_present THEN @name ELSE display_name END,
              email=CASE WHEN @email_present THEN @email ELSE email END, phone=CASE WHEN @phone_present THEN @phone ELSE phone END,
              version=version+1,updated_at=now()
            WHERE tenant_id=@tenant AND id=@id AND version=@version AND deleted_at IS NULL
            RETURNING id,display_name,email,phone,version,created_at,updated_at
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",id); command.Parameters.AddWithValue("version",version);
        command.Parameters.AddWithValue("name_present",namePresent); command.Parameters.AddWithValue("email_present",emailPresent); command.Parameters.AddWithValue("phone_present",phonePresent);
        command.Parameters.AddWithValue("name",NpgsqlDbType.Text,(object?)name ?? DBNull.Value); command.Parameters.AddWithValue("email",NpgsqlDbType.Text,(object?)email ?? DBNull.Value); command.Parameters.AddWithValue("phone",NpgsqlDbType.Text,(object?)phone ?? DBNull.Value);
        await using var reader=await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public static async Task<AttributeChange> MutateAttributeAsync(NpgsqlConnection connection, NpgsqlTransaction tx, Guid tenant,
        Guid contact, Guid field, string issuer, string subject, long expected, string correlation, string kind, string? raw, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT platform.mutate_contact_attribute(@tenant,@contact,@field,@issuer,@subject,@expected,@correlation,@kind,@raw)", connection, tx);
        cmd.Parameters.AddWithValue("tenant", tenant);
        cmd.Parameters.AddWithValue("contact", contact);
        cmd.Parameters.AddWithValue("field", field);
        cmd.Parameters.AddWithValue("issuer", issuer);
        cmd.Parameters.AddWithValue("subject", subject);
        cmd.Parameters.AddWithValue("expected", expected);
        cmd.Parameters.AddWithValue("correlation", correlation);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("raw", NpgsqlDbType.Text, (object?)raw ?? DBNull.Value);
        return await cmd.ExecuteScalarAsync(ct) switch
        {
            "conflict" => AttributeChange.Conflict,
            "missing" => AttributeChange.Missing,
            "invalid" => AttributeChange.Invalid,
            "unrepresentable" => AttributeChange.Unrepresentable,
            "unchanged" => AttributeChange.Unchanged,
            "changed" => AttributeChange.Changed,
            _ => throw new InvalidDataException("Unexpected attribute mutation result.")
        };
    }

    public static async Task<long?> DeleteAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid tenantId,Guid id,long version,CancellationToken ct)
    {
        await using var command=new NpgsqlCommand("""
            UPDATE crm.contacts SET deleted_at=now(),updated_at=now(),version=version+1
            WHERE tenant_id=@tenant AND id=@id AND version=@version AND deleted_at IS NULL RETURNING version
            """,connection,transaction);
        command.Parameters.AddWithValue("tenant",tenantId); command.Parameters.AddWithValue("id",id); command.Parameters.AddWithValue("version",version);
        return (long?)await command.ExecuteScalarAsync(ct);
    }

    private static ContactRecord Read(NpgsqlDataReader reader) => new(reader.GetGuid(0),reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt64(4),reader.GetDateTime(5),reader.GetDateTime(6));
}
