using System.Text.Json;
using System.Text.RegularExpressions;
using Platform.Api.Identity;
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
