using System.Globalization;
using System.Text.Json;
using Platform.Api.Identity;
using Platform.Api.Operations;
using Platform.Infrastructure.Crm;

namespace Platform.Api.Crm;

internal static class TimelineEndpoints
{
    internal static void MapTimelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/contacts/{id}").RequireAuthorization().WithMetadata(new RequireAccessToken());
        routes.MapGet("/timeline", ListAsync);
        routes.MapPost("/notes", CreateAsync);
    }

    private static Task Error(HttpContext ctx, int status, string reason) =>
        OperationalErrors.WriteProblemAsync(ctx, status, reason, ctx.RequestAborted);

    private static Task ListAsync(HttpContext ctx, IConfiguration config, string id) =>
        TenantEndpointTransaction.RunAsync(ctx, config, false, async (conn, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            if (!Guid.TryParseExact(id, "D", out var contact)) { await Error(ctx, 400, "REQUEST_INVALID"); return; }
            if (await ContactStore.GetAsync(conn, tx, tenant, contact, ct) is null) { await Error(ctx, 404, "NOT_FOUND"); return; }
            var query = ctx.Request.Query;
            var limit = 20;
            if (query.Any(q => q.Key is not ("cursor" or "limit") || q.Value.Count != 1) ||
                (query.TryGetValue("limit", out var limits) && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100)))
            { await Error(ctx, 400, "REQUEST_INVALID"); return; }
            if (!TimelineCursor.TryCreate(config, out var cursor)) { await Error(ctx, 503, "CRM_CURSOR_UNAVAILABLE"); return; }
            DateTime? anchorTime = null;
            Guid? anchorId = null;
            if (query.TryGetValue("cursor", out var token))
            {
                if (!cursor!.TryDecode(token[0]!, tenant, contact, out var time, out var entry)) { await Error(ctx, 400, "CURSOR_INVALID"); return; }
                anchorTime = time;
                anchorId = entry;
            }
            var items = await TimelineStore.ListAsync(conn, tx, tenant, contact, anchorTime, anchorId, limit + 1, ct);
            string? nextCursor = null;
            if (items.Count > limit)
            {
                items.RemoveAt(limit);
                nextCursor = cursor!.Encode(tenant, contact, items[^1].OccurredAt, items[^1].Id);
            }
            await commit();
            await ctx.Response.WriteAsJsonAsync(new { items, nextCursor }, ct);
        }, TenantPermission.ContactsRead, "CRM_STORE_UNAVAILABLE");

    private static async Task CreateAsync(HttpContext ctx, IConfiguration config, string id)
    {
        string text;
        try
        {
            // A fully escaped supplementary scalar takes twelve ASCII bytes.
            // Leave room beyond the 12,000-byte value for JSON syntax and whitespace.
            var body = await ContactEndpoints.ReadAuthorizedBodyAsync(ctx, config, maxBytes: 16 * 1024);
            if (body is null) return;
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Count() != 1) throw new JsonException();
            text = DefinitionEndpoints.Text(doc.RootElement, "text", 1000);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { await Error(ctx, 413, "REQUEST_INVALID"); return; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { await Error(ctx, 400, "REQUEST_INVALID"); return; }

        await TenantEndpointTransaction.RunAsync(ctx, config, true, async (conn, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            if (!Guid.TryParseExact(id, "D", out var contact)) { await Error(ctx, 400, "REQUEST_INVALID"); return; }
            if (await ContactStore.GetAsync(conn, tx, tenant, contact, ct) is null) { await Error(ctx, 404, "NOT_FOUND"); return; }
            var condition = MembershipPreconditions.Parse(ctx.Request.Headers.IfMatch, out var expected);
            if (condition == MembershipPreconditionResult.Missing) { await Error(ctx, 428, "PRECONDITION_REQUIRED"); return; }
            if (condition == MembershipPreconditionResult.Invalid) { await Error(ctx, 400, "PRECONDITION_INVALID"); return; }
            var note = await TimelineStore.CreateNoteAsync(conn, tx, tenant, contact, ctx.User.FindFirst("iss")!.Value,
                ctx.User.FindFirst("sub")!.Value, expected, ctx.TraceIdentifier, text, ct);
            if (note is null) { await Error(ctx, 412, "VERSION_CONFLICT"); return; }
            await commit();
            ctx.Response.Headers.ETag = MembershipPreconditions.Format(note.Value.Version);
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsJsonAsync(note.Value.Entry, ct);
        }, TenantPermission.ContactsWrite, "CRM_STORE_UNAVAILABLE");
    }
}
