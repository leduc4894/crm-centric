using System.Security.Claims;
using Microsoft.Extensions.Primitives;
using Npgsql;
using Platform.Infrastructure.Identity;

namespace Platform.Api.Identity;

public enum TenantPermission { TeamManage, AuditRead, ContactsRead, ContactsWrite }

public static class TenantAuthorization
{
    public static bool HasTeamManage(string role) => role is "owner" or "admin";

    public static bool HasProvisioningScope(ClaimsPrincipal principal)
    {
        var claims = principal.FindAll("scope").ToArray();
        if (claims.Length != 1 || claims[0].Value.Length == 0) return false;
        var scope = claims[0].Value;
        // RFC 6749 scope-token: visible ASCII except double quote and backslash.
        var tokens = scope.Split(' ');
        if (tokens.Any(token => token.Length == 0 || token.Any(c => c < '!' || c > '~' || c is '"' or '\\'))) return false;
        return tokens.Contains("platform.tenants.create", StringComparer.Ordinal);
    }

    public static bool TryGetTenantId(IHeaderDictionary headers, out Guid tenantId)
    {
        tenantId = default;
        if (!headers.TryGetValue("X-Tenant-Id", out StringValues values) || values.Count != 1) return false;
        var value = values[0];
        return Guid.TryParseExact(value, "D", out tenantId) && string.Equals(value, tenantId.ToString("D"), StringComparison.Ordinal);
    }

    // Only call after a verified transaction has established the RLS tenant context.
    public static async Task<string?> GetActiveRoleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, string issuer, string subject, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            SELECT role FROM platform.memberships
            WHERE tenant_id=@tenant AND issuer=@issuer AND subject=@subject AND status='active'
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("issuer", issuer);
        command.Parameters.AddWithValue("subject", subject);
        return (string?)await command.ExecuteScalarAsync(ct);
    }
    public sealed record AuthorizationResult(Guid TenantId, NpgsqlTransaction? Transaction, string? ReasonToken);

    // Caller owns the returned transaction. Principal must already be authenticated.
    // A locked, operator-verified staff binding and active tenant membership are both required.
    public static async Task<AuthorizationResult> BeginAsync(HttpContext context, NpgsqlConnection connection, bool mutation, CancellationToken ct,
        TenantPermission permission = TenantPermission.TeamManage)
    {
        if (!TryGetTenantId(context.Request.Headers, out var tenantId))
            return new(default, null, "TENANT_CONTEXT_INVALID");
        var issuer = context.User.FindFirst("iss")?.Value;
        var subject = context.User.FindFirst("sub")?.Value;
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
            return new(tenantId, null, "TENANT_ACCESS_DENIED");
        NpgsqlTransaction transaction;
        try
        {
            transaction = mutation
                ? await MembershipMutationTransaction.BeginAsync(connection, issuer, subject, tenantId, ct)
                : await VerifiedTenantTransaction.BeginAsync(connection, issuer, subject, tenantId, ct);
        }
        catch (UnauthorizedAccessException)
        {
            return new(tenantId, null, "TENANT_ACCESS_DENIED");
        }
        try
        {
            await using var staff = new NpgsqlCommand("SELECT platform.lock_verified_staff(@issuer,@subject)", connection, transaction);
            staff.Parameters.AddWithValue("issuer", issuer);
            staff.Parameters.AddWithValue("subject", subject);
            if (await staff.ExecuteScalarAsync(ct) is not true)
            {
                await transaction.DisposeAsync();
                return new(tenantId, null, "TENANT_ACCESS_DENIED");
            }
            var role = await GetActiveRoleAsync(connection, transaction, tenantId, issuer, subject, ct);
            var permitted = permission switch
            {
                TenantPermission.TeamManage => role is not null && HasTeamManage(role),
                TenantPermission.AuditRead => role is "owner" or "admin",
                TenantPermission.ContactsRead => role is "owner" or "admin" or "agent" or "operator",
                TenantPermission.ContactsWrite => role is "owner" or "admin" or "agent",
                _ => false
            };
            if (permitted) return new(tenantId, transaction, null);
            await transaction.DisposeAsync();
            return new(tenantId, null, "INSUFFICIENT_SCOPE");
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

}
