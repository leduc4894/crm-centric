using System.Globalization;
using Platform.Api.Operations;
using Platform.Infrastructure.Identity;

namespace Platform.Api.Identity;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/audit", ListAsync).RequireAuthorization();
        return endpoints;
    }

    private static Task ListAsync(HttpContext c, IConfiguration config) =>
        TenantEndpointTransaction.RunAsync(c, config, false, async (connection, transaction, tenant, commit) =>
        {
            var query = c.Request.Query;
            var limit = 20;
            if (query.TryGetValue("limit", out var limits) && (limits.Count != 1 ||
                !int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
            {
                await OperationalErrors.WriteProblemAsync(c, 400, "REQUEST_INVALID", c.RequestAborted);
                return;
            }

            DateTime? timestamp = null;
            Guid? id = null;
            if (query.TryGetValue("cursor", out var cursors))
            {
                if (cursors.Count != 1)
                {
                    await OperationalErrors.WriteProblemAsync(c, 400, "REQUEST_INVALID", c.RequestAborted);
                    return;
                }
                if (!AuditCursor.TryDecode(cursors[0]!, tenant, out var time, out var entry))
                {
                    await OperationalErrors.WriteProblemAsync(c, 400, "CURSOR_INVALID", c.RequestAborted);
                    return;
                }
                timestamp = time;
                id = entry;
            }

            var items = await AuditStore.ListAsync(connection, transaction, tenant, c.User.FindFirst("iss")!.Value,
                c.User.FindFirst("sub")!.Value, timestamp, id, limit, c.RequestAborted);
            string? nextCursor = null;
            if (items.Count > limit)
            {
                items.RemoveAt(limit);
                nextCursor = AuditCursor.Encode(tenant, items[^1].Timestamp, items[^1].Id);
            }
            await commit();
            c.Response.Headers.CacheControl = "no-store";
            await c.Response.WriteAsJsonAsync(new { items, nextCursor }, c.RequestAborted);
        }, TenantPermission.AuditRead);
}
