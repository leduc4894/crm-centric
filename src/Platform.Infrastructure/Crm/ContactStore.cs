using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Crm;

public sealed record ContactRecord(Guid Id, string DisplayName, string? Email, string? Phone, long Version, DateTime CreatedAt, DateTime UpdatedAt);

public static class ContactStore
{
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

    private static ContactRecord Read(NpgsqlDataReader reader) => new(reader.GetGuid(0),reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetInt64(4),reader.GetDateTime(5),reader.GetDateTime(6));
}
