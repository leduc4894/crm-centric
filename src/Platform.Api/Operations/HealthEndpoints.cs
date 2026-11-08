using Platform.Infrastructure.Persistence;

namespace Platform.Api.Operations;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .AllowAnonymous();
        endpoints.MapGet("/health/ready", async (DatabaseReadiness readiness, HttpContext context) =>
        {
            var result = await readiness.CheckAsync(context.RequestAborted);
            context.RequestAborted.ThrowIfCancellationRequested();
            if (result == DatabaseReadinessResult.Ready)
                return Results.Ok(new { status = "ready" });

            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                type: "about:blank", title: "Dependency unavailable",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonToken"] = result == DatabaseReadinessResult.SchemaNotReady
                        ? "DATABASE_SCHEMA_NOT_READY" : "DATABASE_UNAVAILABLE",
                    ["correlationId"] = context.TraceIdentifier
                });
        }).AllowAnonymous();
        return endpoints;
    }
}
