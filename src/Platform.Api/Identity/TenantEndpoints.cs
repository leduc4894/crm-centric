using System.Data;
using System.Globalization;
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
        return endpoints;
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
