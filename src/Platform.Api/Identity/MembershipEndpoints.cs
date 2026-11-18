using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using Platform.Api.Operations;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Identity;

namespace Platform.Api.Identity;

public static class MembershipEndpoints
{
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/memberships", ListAsync).RequireAuthorization();
        endpoints.MapGet("/api/v1/memberships/{id}", GetAsync).RequireAuthorization();
        endpoints.MapPatch("/api/v1/memberships/{id}", PatchAsync).RequireAuthorization();
        return endpoints;
    }
    private sealed record ResponseMembership(Guid Id, string Issuer, string Subject, string Role, string Status, long Version);
    private static ResponseMembership Represent(LifecycleMembership m)
    {
        // Legacy rows can predate the public contract. Never truncate an identity.
        if (m.Subject.EnumerateRunes().Count() is < 1 or > 256 || !Uri.TryCreate(m.Issuer, UriKind.Absolute, out _))
            throw new InvalidDataException("Stored membership is not representable by the contract.");
        return new(m.Id, m.Issuer, m.Subject, m.Role, m.Status switch { "active" => "active", "inactive" => "revoked", _ => throw new InvalidDataException("Unknown membership state.") }, m.Version);
    }
    private static async Task Error(HttpContext context, int status, string reason) => await OperationalErrors.WriteProblemAsync(context, status, reason, context.RequestAborted);
    private static async Task RunAsync(HttpContext context, IConfiguration configuration, bool mutation, Func<NpgsqlConnection, NpgsqlTransaction, Guid, Func<Task>, Task> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            var commitStarted = false;
            try
            {
                await using var connection = new NpgsqlConnection(DatabaseOptions.Parse(configuration.GetConnectionString("Platform")).ConnectionString);
                await connection.OpenAsync(context.RequestAborted);
                var auth = await TenantAuthorization.BeginAsync(context, connection, mutation, context.RequestAborted);
                if (auth.Transaction is null)
                {
                    await Error(context, auth.ReasonToken == "TENANT_CONTEXT_INVALID" ? 400 : 403, auth.ReasonToken!);
                    return;
                }
                await using var transaction = auth.Transaction;
                await operation(connection, transaction, auth.TenantId, async () => { commitStarted = true; await transaction.CommitAsync(context.RequestAborted); });
                return;
            }
            catch (PostgresException ex) when (ex.SqlState is "40P01" or "40001" && attempt < 2 && !commitStarted && !context.Response.HasStarted)
            {
                await Task.Delay(25 * (attempt + 1), context.RequestAborted);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidDataException)
            {
                if (!context.Response.HasStarted) await Error(context, 503, "IDENTITY_STORE_UNAVAILABLE");
                return;
            }
        }
    }
    private static async Task<LifecycleMembership?> Fetch(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenant, Guid id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SELECT id,issuer,subject,role,status,version FROM platform.memberships WHERE tenant_id=@tenant AND id=@id", connection, transaction);
        command.Parameters.AddWithValue("tenant",tenant); command.Parameters.AddWithValue("id",id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt64(5)) : null;
    }
    private static async Task GetAsync(HttpContext context, IConfiguration configuration, string id)
    {
        await RunAsync(context, configuration, false, async (connection, transaction, tenant, commit) =>
        {
            if (!Guid.TryParseExact(id,"D",out var parsed)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var row = await Fetch(connection,transaction,tenant,parsed,context.RequestAborted);
            if (row is null) { await Error(context,404,"NOT_FOUND"); return; }
            var result = Represent(row);
            await commit();
            context.Response.Headers.ETag = MembershipPreconditions.Format(row.Version);
            await context.Response.WriteAsJsonAsync(result,cancellationToken:context.RequestAborted);
        });
    }
    private static async Task ListAsync(HttpContext context, IConfiguration configuration)
    {
        await RunAsync(context, configuration, false, async (connection, transaction, tenant, commit) =>
        {
            var query=context.Request.Query;
            if ((query.TryGetValue("limit",out var limits) && limits.Count != 1) || (query.TryGetValue("cursor",out var cursors) && cursors.Count != 1)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var limit=20;
            if (query.ContainsKey("limit") && (!int.TryParse(query["limit"],NumberStyles.None,CultureInfo.InvariantCulture,out limit) || limit is < 1 or > 100)) { await Error(context,400,"REQUEST_INVALID"); return; }
            Guid? anchor=null;
            if (query.TryGetValue("cursor",out var cursor)) { if (!MembershipCursor.TryDecode(cursor.ToString(),tenant,out var decoded)) { await Error(context,400,"CURSOR_INVALID"); return; } anchor=decoded; }
            await using var command=new NpgsqlCommand("SELECT id,issuer,subject,role,status,version FROM platform.memberships WHERE tenant_id=@tenant AND (@anchor IS NULL OR id>@anchor) ORDER BY id LIMIT @limit",connection,transaction);
            command.Parameters.AddWithValue("tenant",tenant); command.Parameters.AddWithValue("anchor",NpgsqlTypes.NpgsqlDbType.Uuid,(object?)anchor ?? DBNull.Value); command.Parameters.AddWithValue("limit",limit+1);
            var rows=new List<LifecycleMembership>();
            await using (var reader=await command.ExecuteReaderAsync(context.RequestAborted)) while(await reader.ReadAsync(context.RequestAborted)) rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetInt64(5)));
            var items=rows.Take(limit).Select(Represent).ToArray(); var nextCursor=rows.Count>limit ? MembershipCursor.Encode(tenant,items[^1].Id) : null;
            await commit();
            await context.Response.WriteAsJsonAsync(new { items,nextCursor },cancellationToken:context.RequestAborted);
        });
    }
    private static async Task PatchAsync(HttpContext context, IConfiguration configuration, string id)
    {
        byte[]? cachedBody = null;
        await RunAsync(context, configuration, true, async (connection, transaction, tenant, commit) =>
        {
            if (!Guid.TryParseExact(id, "D", out var parsed)) { await Error(context,400,"REQUEST_INVALID"); return; }
            var current = await Fetch(connection, transaction, tenant, parsed, context.RequestAborted);
            if (current is null) { await Error(context,404,"NOT_FOUND"); return; }
            _ = Represent(current);
            var precondition = MembershipPreconditions.Parse(context.Request.Headers.IfMatch, out var version);
            if (precondition == MembershipPreconditionResult.Missing) { await Error(context,428,"PRECONDITION_REQUIRED"); return; }
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(context,400,"PRECONDITION_INVALID"); return; }
            if (!context.Request.HasJsonContentType()) { await Error(context,415,"REQUEST_INVALID"); return; }
            string? role = null, status = null;
            try
            {
                const int maxBytes = 8192;
                if (cachedBody is null)
                {
                    using var buffer = new MemoryStream(); var chunk = new byte[1024]; int count;
                    while ((count = await context.Request.Body.ReadAsync(chunk,context.RequestAborted)) != 0)
                    {
                        if (buffer.Length + count > maxBytes) { await Error(context,413,"REQUEST_INVALID"); return; }
                        buffer.Write(chunk,0,count);
                    }
                    cachedBody = buffer.ToArray();
                }
                using var doc=JsonDocument.Parse(cachedBody);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    string name;
                    try { name=property.Name; } catch (InvalidOperationException) { throw new JsonException(); }
                    if (!seen.Add(name) || property.Value.ValueKind != JsonValueKind.String) throw new JsonException();
                    string? value;
                    try { value=property.Value.GetString(); } catch (InvalidOperationException) { throw new JsonException(); }
                    if (name == "role" && value is "owner" or "admin" or "agent" or "operator") role=value;
                    else if (name == "status" && value is "active" or "revoked") status=value == "revoked" ? "inactive" : value;
                    else throw new JsonException();
                }
                if (seen.Count == 0) throw new JsonException();
            }
            catch (JsonException) { await Error(context,400,"REQUEST_INVALID"); return; }
            var result=await LifecycleStore.UpdateMembershipAsync(connection,transaction,tenant,parsed,version,role,status,context.User.FindFirst("iss")!.Value,context.User.FindFirst("sub")!.Value,context.TraceIdentifier,context.RequestAborted);
            if (result.Membership is null)
            {
                var (statusCode,reason) = result.Outcome switch
                {
                    MembershipUpdateOutcome.NotFound => (404,"NOT_FOUND"),
                    MembershipUpdateOutcome.Forbidden => (403,"INSUFFICIENT_SCOPE"),
                    MembershipUpdateOutcome.VersionConflict => (412,"VERSION_CONFLICT"),
                    MembershipUpdateOutcome.OwnerInvariant => (409,"OWNER_INVARIANT"),
                    _ => (400,"REQUEST_INVALID")
                };
                await Error(context,statusCode,reason); return;
            }
            var response=Represent(result.Membership);
            await commit();
            context.Response.Headers.ETag=MembershipPreconditions.Format(result.Membership.Version);
            await context.Response.WriteAsJsonAsync(response,cancellationToken:context.RequestAborted);
        });
    }

}
