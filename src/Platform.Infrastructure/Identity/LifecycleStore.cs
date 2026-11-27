using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Identity;

public sealed record LifecycleMembership(Guid Id, string Issuer, string Subject, string Role, string Status, long Version);
public enum MembershipUpdateOutcome { Updated, Unchanged, NotFound, Forbidden, VersionConflict, OwnerInvariant, Invalid }
public sealed record MembershipUpdateResult(MembershipUpdateOutcome Outcome, LifecycleMembership? Membership);

// Actor identity is supplied by the authenticated API. Database credentials are trusted
// server credentials and do not independently authenticate issuer or subject.
public static class LifecycleStore
{
    public static async Task CreateTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        string name, string issuer, string subject, string correlationId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT platform.create_tenant_with_owner(@tenant,@name,@issuer,@subject,@correlation)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("correlation", correlationId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task<MembershipUpdateResult> UpdateMembershipAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid targetId, long expectedVersion, string? role, string? status,
        string issuer, string subject, string correlationId, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT * FROM platform.update_membership(@tenant,@target,@version,@role,@status,@issuer,@subject,@correlation)", connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("target", targetId);
        command.Parameters.AddWithValue("version", expectedVersion);
        command.Parameters.AddWithValue("role", NpgsqlDbType.Text, (object?)role ?? DBNull.Value);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("correlation", correlationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("Lifecycle function returned no outcome.");
        var outcome = reader.GetString(0) switch
        {
            "updated" => MembershipUpdateOutcome.Updated,
            "unchanged" => MembershipUpdateOutcome.Unchanged,
            "not_found" => MembershipUpdateOutcome.NotFound,
            "forbidden" => MembershipUpdateOutcome.Forbidden,
            "version_conflict" => MembershipUpdateOutcome.VersionConflict,
            "owner_invariant" => MembershipUpdateOutcome.OwnerInvariant,
            "invalid" => MembershipUpdateOutcome.Invalid,
            _ => throw new InvalidOperationException("Lifecycle function returned an unknown outcome.")
        };
        return new(outcome, reader.IsDBNull(1) ? null : new(reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6)));
    }
}
