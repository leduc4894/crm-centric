using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Identity;

public sealed record InviteView(Guid Id, string Email, string Role, DateTime ExpiresAt, string Status);

public sealed record InviteAcceptance(string Outcome, LifecycleMembership? Membership);

public sealed class InviteStore(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant, string issuer, string subject)
{
    private NpgsqlCommand Command(string sql)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        return command;
    }

    public async Task<InviteView> CreateAsync(byte[] hash, string email, string normalized, string role, string correlation, CancellationToken ct)
    {
        await using var cmd = Command("SELECT id,expires_at FROM platform.create_invite(@tenant,@issuer,@subject,@correlation,@hash,@email,@normalized,@role)");
        cmd.Parameters.AddWithValue("correlation", correlation);
        cmd.Parameters.AddWithValue("hash", hash);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("normalized", normalized);
        cmd.Parameters.AddWithValue("role", role);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Missing invitation result.");
        return new(reader.GetGuid(0), email, role, reader.GetDateTime(1), "pending");
    }

    public async Task<List<InviteView>> ListAsync(Guid? anchor, int limit, CancellationToken ct)
    {
        await using var cmd = Command("SELECT id,email,role,expires_at,status FROM platform.list_invites(@tenant,@issuer,@subject,@anchor,@take)");
        cmd.Parameters.AddWithValue("anchor", NpgsqlDbType.Uuid, (object?)anchor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("take", limit + 1);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var items = new List<InviteView>();
        while (await reader.ReadAsync(ct)) items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3), reader.GetString(4)));
        return items;
    }

    public async Task<string> RevokeAsync(Guid id, string correlation, CancellationToken ct)
    {
        await using var cmd = Command("SELECT platform.revoke_invite(@tenant,@issuer,@subject,@correlation,@id)");
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("correlation", correlation);
        return (string?)await cmd.ExecuteScalarAsync(ct) ?? throw new InvalidDataException("Missing invitation outcome.");
    }

    public static async Task<InviteAcceptance> AcceptAsync(NpgsqlConnection connection, byte[] tokenHash, string issuer,
        string subject, string normalizedEmail, string correlationId, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var commitStarted = false;
            try
            {
                await using var tx = await connection.BeginTransactionAsync(ct);
                await using var cmd = new NpgsqlCommand(
                    "SELECT outcome,member_id,invited_role FROM platform.accept_invite(@hash,@issuer,@subject,@email,@correlation)", connection, tx);
                cmd.Parameters.AddWithValue("hash", tokenHash);
                cmd.Parameters.AddWithValue("issuer", issuer);
                cmd.Parameters.AddWithValue("subject", subject);
                cmd.Parameters.AddWithValue("email", normalizedEmail);
                cmd.Parameters.AddWithValue("correlation", correlationId);
                LifecycleMembership membership;
                await using (var reader = await cmd.ExecuteReaderAsync(ct))
                {
                    if (!await reader.ReadAsync(ct)) throw new InvalidDataException("Missing invitation outcome.");
                    var outcome = reader.GetString(0);
                    if (outcome != "accepted") return new(outcome, null);
                    membership = new(reader.GetGuid(1), issuer, subject, reader.GetString(2), "active", 1);
                }
                commitStarted = true;
                await tx.CommitAsync(ct);
                return new("accepted", membership);
            }
            catch (PostgresException ex) when (!commitStarted && attempt < 2 && ex.SqlState is "40P01" or "40001")
            {
                await Task.Delay(25 * (attempt + 1), ct);
            }
        }
    }
}
