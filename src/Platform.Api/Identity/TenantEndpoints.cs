using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Npgsql;
using Platform.Api.Operations;
using Platform.Infrastructure.Configuration;
using Platform.Infrastructure.Identity;

namespace Platform.Api.Identity;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/tenants", ListAsync).RequireAuthorization();
        endpoints.MapPost("/api/v1/tenants", CreateAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task CreateAsync(HttpContext context, IConfiguration configuration)
    {
        if (!TenantAuthorization.HasProvisioningScope(context.User))
        {
            await OperationalErrors.WriteProblemAsync(context, 403, "INSUFFICIENT_SCOPE", context.RequestAborted);
            return;
        }
        if (!context.Request.HasJsonContentType())
        {
            await OperationalErrors.WriteProblemAsync(context, 415, "REQUEST_INVALID", context.RequestAborted);
            return;
        }
        // Bound request buffering and reject duplicate/unknown properties rather than allowing
        // the JSON parser or model binder to silently select one value.
        const int maxBytes = 8192;
        string? name = null;
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[1024];
            int count;
            while ((count = await context.Request.Body.ReadAsync(chunk, context.RequestAborted)) != 0)
            {
                if (buffer.Length + count > maxBytes)
                {
                    await OperationalErrors.WriteProblemAsync(context, 413, "REQUEST_INVALID", context.RequestAborted);
                    return;
                }
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException();
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 1) throw new JsonException();
            string propertyName;
            try { propertyName = properties[0].Name; }
            catch (InvalidOperationException) { throw new JsonException("Invalid Unicode property name."); }
            if (propertyName != "name" || properties[0].Value.ValueKind != JsonValueKind.String)
                throw new JsonException();
            try { name = properties[0].Value.GetString(); }
            catch (InvalidOperationException) { throw new JsonException("Invalid Unicode string."); }
            if (string.IsNullOrWhiteSpace(name) || name.EnumerateRunes().Count() > 200 || name.Contains('\0'))
                throw new JsonException();
        }
        catch (JsonException)
        {
            await OperationalErrors.WriteProblemAsync(context, 400, "REQUEST_INVALID", context.RequestAborted);
            return;
        }

        var id = Guid.NewGuid();
        var issuer = context.User.FindFirst("iss")!.Value;
        var subject = context.User.FindFirst("sub")!.Value;
        if (subject.EnumerateRunes().Count() > 256)
        {
            await OperationalErrors.WriteProblemAsync(context, 401, "AUTHENTICATION_REQUIRED", context.RequestAborted);
            return;
        }
        try
        {
            await using var connection = new NpgsqlConnection(DatabaseOptions.Parse(configuration.GetConnectionString("Platform")).ConnectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await LifecycleStore.CreateTenantAsync(connection, transaction, id, name, issuer, subject, context.TraceIdentifier, context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(new { id, name, status = "active" }, cancellationToken: context.RequestAborted);
        }
        catch (NpgsqlException)
        {
            await OperationalErrors.WriteProblemAsync(context, 503, "IDENTITY_STORE_UNAVAILABLE", context.RequestAborted);
        }
        catch (TimeoutException)
        {
            await OperationalErrors.WriteProblemAsync(context, 503, "IDENTITY_STORE_UNAVAILABLE", context.RequestAborted);
        }
    }

    private static async Task ListAsync(HttpContext context, IConfiguration configuration)
    {
        var query = context.Request.Query;
        if ((query.TryGetValue("limit", out var limits) && limits.Count != 1) ||
            (query.TryGetValue("cursor", out var cursors) && cursors.Count != 1))
        {
            await OperationalErrors.WriteProblemAsync(context, 400, "REQUEST_INVALID", context.RequestAborted);
            return;
        }
        var limit = 20;
        if (query.ContainsKey("limit") && (!int.TryParse(query["limit"], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
        {
            await OperationalErrors.WriteProblemAsync(context, 400, "REQUEST_INVALID", context.RequestAborted);
            return;
        }
        Guid? cursor = null;
        if (query.TryGetValue("cursor", out var rawCursor))
        {
            if (!TenantCursor.TryDecode(rawCursor.ToString(), out var decoded))
            {
                await OperationalErrors.WriteProblemAsync(context, 400, "CURSOR_INVALID", context.RequestAborted);
                return;
            }
            cursor = decoded;
        }
        var issuer = context.User.FindFirst("iss")!.Value;
        var subject = context.User.FindFirst("sub")!.Value;
        try
        {
            await using var connection = new NpgsqlConnection(DatabaseOptions.Parse(configuration.GetConnectionString("Platform")).ConnectionString);
            await connection.OpenAsync(context.RequestAborted);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, context.RequestAborted);
            if (cursor is { } position)
            {
                var previous = TenantCursor.Predecessor(position);
                var match = await MembershipLookup.ListAsync(connection, issuer, subject, previous, 1, context.RequestAborted);
                if (match.Count == 0 || match[0].Id != position)
                {
                    await OperationalErrors.WriteProblemAsync(context, 400, "CURSOR_INVALID", context.RequestAborted);
                    return;
                }
            }
            var rows = await MembershipLookup.ListAsync(connection, issuer, subject, cursor, limit + 1, context.RequestAborted);
            var items = rows.Take(limit).ToArray();
            var nextCursor = rows.Count > limit ? TenantCursor.Encode(items[^1].Id) : null;
            await transaction.CommitAsync(context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new { items, nextCursor }, cancellationToken: context.RequestAborted);
        }
        catch (NpgsqlException)
        {
            await OperationalErrors.WriteProblemAsync(context, 503, "IDENTITY_STORE_UNAVAILABLE", context.RequestAborted);
        }
        catch (TimeoutException)
        {
            await OperationalErrors.WriteProblemAsync(context, 503, "IDENTITY_STORE_UNAVAILABLE", context.RequestAborted);
        }
    }
}
