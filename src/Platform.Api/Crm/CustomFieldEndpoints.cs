using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Api.Identity;
using Platform.Api.Operations;
using Platform.Infrastructure.Crm;

namespace Platform.Api.Crm;

internal static partial class CustomFieldEndpoints
{
    internal static void MapCustomFieldEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var routes = endpoints.MapGroup("/api/v1/custom-fields").RequireAuthorization().WithMetadata(new RequireAccessToken());
        routes.MapGet("", (HttpContext ctx, IConfiguration config) =>
            DefinitionEndpoints.ListAsync<FieldDefinition>(ctx, config, "field-definitions", CustomFieldStore.ListAsync));
        routes.MapPost("", (HttpContext ctx, IConfiguration config) =>
            DefinitionEndpoints.CreateAsync(ctx, config, "field-definitions", TenantPermission.FieldsManage, Parse));

        var values = endpoints.MapGroup("/api/v1/contacts/{id}/fields/{fieldId}").RequireAuthorization().WithMetadata(new RequireAccessToken());
        values.MapGet("", ReadValueAsync);
        values.MapPut("", WriteValueAsync);
    }

    private static Task ReadValueAsync(HttpContext ctx, IConfiguration config, string id, string fieldId) =>
        TenantEndpointTransaction.RunAsync(ctx, config, false, async (connection, tx, tenant, commit) =>
        {
            var ct = ctx.RequestAborted;
            if (!Guid.TryParseExact(id, "D", out var contact) || !Guid.TryParseExact(fieldId, "D", out var field))
            { await OperationalErrors.WriteProblemAsync(ctx, 400, "REQUEST_INVALID", ct); return; }
            var json = await CustomFieldStore.GetValueAsync(connection, tx, tenant, contact, field, ct);
            if (json is null) { await OperationalErrors.WriteProblemAsync(ctx, 404, "NOT_FOUND", ct); return; }
            using var value = JsonDocument.Parse(json);
            await commit();
            await ctx.Response.WriteAsJsonAsync(new { value = value.RootElement }, ct);
        }, TenantPermission.ContactsRead, "CRM_STORE_UNAVAILABLE");

    private static async Task WriteValueAsync(HttpContext ctx, IConfiguration config, string id, string fieldId)
    {
        if (!await ContactEndpoints.AuthorizeJsonBodyAsync(ctx, config, TenantPermission.ContactsWrite)) return;
        string raw;
        try
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var body = doc.RootElement;
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("value", out var value)
                || body.EnumerateObject().Count() != 1 || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                throw new JsonException();
            // Canonicalize string escapes so PostgreSQL can compare even strings containing U+0000.
            raw = value.ValueKind == JsonValueKind.String ? JsonSerializer.Serialize(value.GetString()) : value.GetRawText();
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == 413)
        { await OperationalErrors.WriteProblemAsync(ctx, 413, "REQUEST_INVALID", ctx.RequestAborted); return; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { await OperationalErrors.WriteProblemAsync(ctx, 400, "REQUEST_INVALID", ctx.RequestAborted); return; }
        await ContactAttributeMutation.RunAsync(ctx, config, id, fieldId, (connection, tx, tenant, contact, field, issuer, subject, ct) =>
            CustomFieldStore.SetValueAsync(connection, tx, tenant, contact, field, issuer, subject, raw, ct));
    }

    [GeneratedRegex(@"\A[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex FieldKey();

    private static DefinitionInput Parse(JsonElement body)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var prop in body.EnumerateObject())
            if (prop.Name is not ("key" or "displayName" or "dataType" or "required") || !keys.Add(prop.Name)) throw new JsonException();
        var key = DefinitionEndpoints.Text(body, "key", 64);
        var label = DefinitionEndpoints.Text(body, "displayName", 200);
        var type = DefinitionEndpoints.Text(body, "dataType", 7);
        if (!FieldKey().IsMatch(key) || type is not ("string" or "number" or "boolean" or "date")) throw new JsonException();
        return new(label, key, type, body.TryGetProperty("required", out var required) && required.GetBoolean());
    }
}
