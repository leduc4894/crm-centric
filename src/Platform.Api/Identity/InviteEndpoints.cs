using System.Globalization;
using System.Text.Json;
using Platform.Api.Operations;
using Platform.Infrastructure.Identity;

namespace Platform.Api.Identity;

public static class InviteEndpoints
{
    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/invites").RequireAuthorization();
        group.MapPost("", CreateAsync);
        group.MapGet("", ListAsync);
        group.MapDelete("/{id}", RevokeAsync);
        return endpoints;
    }

    private static Task Error(HttpContext c, int status, string reason = "REQUEST_INVALID") =>
        OperationalErrors.WriteProblemAsync(c, status, reason, c.RequestAborted);

    private static Task Run(HttpContext c, IConfiguration config, bool mutation, Func<InviteStore, Guid, Func<Task>, Task> action) =>
        TenantEndpointTransaction.RunAsync(c, config, mutation, (connection, transaction, tenant, commit) =>
            action(new(connection, transaction, tenant, c.User.FindFirst("iss")!.Value, c.User.FindFirst("sub")!.Value), tenant, commit));

    private static async Task CreateAsync(HttpContext c, IConfiguration config)
    {
        // Finish bounded client I/O before the tenant-wide mutation transaction.
        // Report validation errors after tenant authorization, like the other management operations.
        const int maxBytes = 8192;
        var jsonContent = c.Request.HasJsonContentType();
        var body = jsonContent ? new byte[maxBytes + 1] : Array.Empty<byte>();
        var length = 0;
        while (length < body.Length)
        {
            var count = await c.Request.Body.ReadAsync(body.AsMemory(length), c.RequestAborted);
            if (count == 0) break;
            length += count;
        }

        await Run(c, config, true, async (store, _, commit) =>
        {
            if (!jsonContent) { await Error(c, 415); return; }
            if (length > maxBytes) { await Error(c, 413); return; }
            string? email = null, role = null;
            try
            {
                using var doc = JsonDocument.Parse(body.AsMemory(0, length));
                if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                foreach (var field in doc.RootElement.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.String) throw new JsonException();
                    if (field.Name == "email" && email is null) email = field.Value.GetString();
                    else if (field.Name == "role" && role is null) role = field.Value.GetString();
                    else throw new JsonException();
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException) { await Error(c, 400); return; }
            if (role is not ("admin" or "agent" or "operator") || !InviteEmail.TryNormalize(email!, out var normalized))
            { await Error(c, 400); return; }
            var (token, hash) = InviteToken.Create();
            var row = await store.CreateAsync(hash, email!, normalized, role, c.TraceIdentifier, c.RequestAborted);
            await commit();
            c.Response.StatusCode = 201;
            await c.Response.WriteAsJsonAsync(new { row.Id, row.Email, row.Role, row.ExpiresAt, row.Status, token }, c.RequestAborted);
        });
    }

    private static Task ListAsync(HttpContext c, IConfiguration config) => Run(c, config, false, async (store, tenant, commit) =>
    {
        var query = c.Request.Query;
        var limit = 20;
        if (query.TryGetValue("limit", out var limits) && (limits.Count != 1 || !int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
        { await Error(c, 400); return; }
        Guid? anchor = null;
        if (query.TryGetValue("cursor", out var cursors))
        {
            if (cursors.Count != 1) { await Error(c, 400); return; }
            if (!InviteCursor.TryDecode(cursors[0]!, tenant, out var id)) { await Error(c, 400, "CURSOR_INVALID"); return; }
            anchor = id;
        }
        var items = await store.ListAsync(anchor, limit, c.RequestAborted);
        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(limit);
            nextCursor = InviteCursor.Encode(tenant, items[^1].Id);
        }
        await commit();
        await c.Response.WriteAsJsonAsync(new { items, nextCursor }, c.RequestAborted);
    });

    private static Task RevokeAsync(HttpContext c, IConfiguration config, string id) => Run(c, config, true, async (store, _, commit) =>
    {
        if (!Guid.TryParseExact(id, "D", out var target)) { await Error(c, 400); return; }
        var result = await store.RevokeAsync(target, c.TraceIdentifier, c.RequestAborted);
        if (result == "INVITE_NOT_FOUND") { await Error(c, 404, result); return; }
        if (result == "INVITE_STATE_CONFLICT") { await Error(c, 409, result); return; }
        if (result != "revoked") throw new InvalidDataException("Unknown invitation outcome.");
        await commit();
        c.Response.StatusCode = 204;
    });
}
