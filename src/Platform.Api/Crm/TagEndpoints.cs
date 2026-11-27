using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using Platform.Api.Identity;
using Platform.Api.Operations;
using Platform.Infrastructure.Crm;

namespace Platform.Api.Crm;

internal static class TagEndpoints
{
    internal static void MapTagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/tags").RequireAuthorization().WithMetadata(new RequireAccessToken());
        routes.MapGet("", (HttpContext ctx, IConfiguration config) =>
            DefinitionEndpoints.ListAsync<TagRecord>(ctx, config, "tags", TagStore.ListAsync));
        routes.MapPost("", (HttpContext ctx, IConfiguration config) =>
            DefinitionEndpoints.CreateAsync(ctx, config, "tags", TenantPermission.ContactsWrite, Parse));

        var contacts = endpoints.MapGroup("/api/v1/contacts/{id}/tags").RequireAuthorization().WithMetadata(new RequireAccessToken());
        contacts.MapGet("", ListContactTagsAsync);
        contacts.MapMethods("/{tagId}", ["PUT", "DELETE"], SetContactTagAsync);
    }

    private static Task ListContactTagsAsync(HttpContext ctx, IConfiguration config, string id) =>
        TenantEndpointTransaction.RunAsync(ctx, config, false, async (connection, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            if (!Guid.TryParseExact(id, "D", out var contact))
            { await OperationalErrors.WriteProblemAsync(ctx, 400, "REQUEST_INVALID", ct); return; }
            if (await ContactStore.GetAsync(connection, tx, tenant, contact, ct) is null)
            { await OperationalErrors.WriteProblemAsync(ctx, 404, "NOT_FOUND", ct); return; }
            await DefinitionEndpoints.WritePageAsync(ctx, config, tenant, "contact-tags", contact,
                (after, limit) => TagStore.ListAsync(connection, tx, tenant, contact, after, limit, ct), commit);
        }, TenantPermission.ContactsRead, "CRM_STORE_UNAVAILABLE");

    private static Task SetContactTagAsync(HttpContext ctx, IConfiguration config, string id, string tagId) =>
        TenantEndpointTransaction.RunAsync(ctx, config, true, async (connection, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            Task Error(int status, string token) => OperationalErrors.WriteProblemAsync(ctx, status, token, ct);
            if (!Guid.TryParseExact(id, "D", out var contact) || !Guid.TryParseExact(tagId, "D", out var tag))
            { await Error(400, "REQUEST_INVALID"); return; }
            var row = await ContactStore.LockByIdAsync(connection, tx, tenant, contact, ct);
            if (row is null) { await Error(404, "NOT_FOUND"); return; }
            var precondition = MembershipPreconditions.Parse(ctx.Request.Headers.IfMatch, out var version);
            if (precondition == MembershipPreconditionResult.Missing) { await Error(428, "PRECONDITION_REQUIRED"); return; }
            if (precondition == MembershipPreconditionResult.Invalid) { await Error(400, "PRECONDITION_INVALID"); return; }
            if (version != row.Contact.Version) { await Error(412, "VERSION_CONFLICT"); return; }
            if (row.Deleted) { await Error(404, "NOT_FOUND"); return; }

            var issuer = ctx.User.FindFirst("iss")!.Value;
            var subject = ctx.User.FindFirst("sub")!.Value;
            var changed = await TagStore.SetAssignmentAsync(connection, tx, tenant, contact, tag, issuer, subject,
                HttpMethods.IsPut(ctx.Request.Method), ct);
            if (changed is null) { await Error(404, "NOT_FOUND"); return; }
            var result = row.Contact;
            if (changed.Value)
            {
                result = await ContactStore.AdvanceVersionAsync(connection, tx, tenant, contact, version, ct);
                if (result is null) { await Error(412, "VERSION_CONFLICT"); return; }
                await ContactMutationJournal.AppendAsync(connection, tx, tenant, contact, issuer, subject, "contact.updated",
                    ctx.TraceIdentifier, result.Version, ct);
            }
            await commit();
            ctx.Response.Headers.ETag = MembershipPreconditions.Format(result.Version);
            await ctx.Response.WriteAsJsonAsync(result, ct);
        }, TenantPermission.ContactsWrite, "CRM_STORE_UNAVAILABLE");

    private static DefinitionInput Parse(JsonElement body)
    {
        var label = DefinitionEndpoints.Text(body, "name", 64);
        if (body.EnumerateObject().Count() != 1) throw new JsonException();
        return new(label, null, null, false);
    }
}

internal sealed record DefinitionInput(string Label, string? Key, string? Type, bool Required);

internal static class DefinitionEndpoints
{
    private static Task Error(HttpContext context, int status, string reason) =>
        OperationalErrors.WriteProblemAsync(context, status, reason, context.RequestAborted);

    internal static string Text(JsonElement body, string key, int maxLength)
    {
        if (!body.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.String) throw new JsonException();
        var value = prop.GetString()!;
        if (value.Length == 0 || value.EnumerateRunes().Count() > maxLength || !ContactEndpoints.ValidText(value)) throw new JsonException();
        return value;
    }

    internal static Task ListAsync<T>(HttpContext ctx, IConfiguration config, string kind,
        Func<NpgsqlConnection, NpgsqlTransaction, Guid, Guid?, int, CancellationToken, Task<List<T>>> list) where T : IDefinitionRecord =>
        TenantEndpointTransaction.RunAsync(ctx, config, false, (connection, tx, tenant, commit) =>
            WritePageAsync(ctx, config, tenant, kind, null,
                (after, limit) => list(connection, tx, tenant, after, limit, ctx.RequestAborted), commit),
            TenantPermission.ContactsRead, "CRM_STORE_UNAVAILABLE");

    internal static async Task WritePageAsync<T>(HttpContext ctx, IConfiguration config, Guid tenant, string kind,
        Guid? contact, Func<Guid?, int, Task<List<T>>> list, Func<Task> commit) where T : IDefinitionRecord
    {
        var query = ctx.Request.Query;
        var limit = 20;
        if (query.Any(q => q.Key is not ("limit" or "cursor") || q.Value.Count != 1)
            || (query.TryGetValue("limit", out var limits) && (!int.TryParse(limits[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100)))
        { await Error(ctx, 400, "REQUEST_INVALID"); return; }
        if (!CrmCollectionCursor.TryCreate(config, out var signer)) { await Error(ctx, 503, "CRM_CURSOR_UNAVAILABLE"); return; }
        Guid? after = null;
        if (query.TryGetValue("cursor", out var token))
        {
            if (!signer!.TryDecode(token[0]!, tenant, kind, contact, out var id)) { await Error(ctx, 400, "CURSOR_INVALID"); return; }
            after = id;
        }
        var rows = await list(after, limit + 1);
        string? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(limit);
            next = signer!.Encode(tenant, kind, contact, rows[^1].Id);
        }
        await commit();
        await ctx.Response.WriteAsJsonAsync(new { items = rows, nextCursor = next }, ctx.RequestAborted);
    }

    internal static async Task CreateAsync(HttpContext ctx, IConfiguration config, string kind, TenantPermission permission,
        Func<JsonElement, DefinitionInput> parse)
    {
        var bytes = await ContactEndpoints.ReadAuthorizedBodyAsync(ctx, config, permission);
        if (bytes is null) return;
        DefinitionInput input;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            input = parse(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { await Error(ctx, 400, "REQUEST_INVALID"); return; }
        await TenantEndpointTransaction.RunAsync(ctx, config, true, async (connection, tx, tenant, commit) =>
        {
            var id = await CustomFieldStore.CreateDefinitionAsync(connection, tx, tenant, ctx.User.FindFirst("iss")!.Value,
                ctx.User.FindFirst("sub")!.Value, ctx.TraceIdentifier, kind, input.Label, input.Key, input.Type, input.Required, ctx.RequestAborted);
            if (id is null) { await Error(ctx, 409, kind == "tags" ? "TAG_NAME_CONFLICT" : "FIELD_KEY_CONFLICT"); return; }
            object result = kind == "tags" ? new TagRecord(id.Value, input.Label)
                : new FieldDefinition(id.Value, input.Key!, input.Label, input.Type!, input.Required, 1);
            await commit();
            ctx.Response.StatusCode = 201;
            await ctx.Response.WriteAsJsonAsync(result, ctx.RequestAborted);
        }, permission, "CRM_STORE_UNAVAILABLE");
    }
}
