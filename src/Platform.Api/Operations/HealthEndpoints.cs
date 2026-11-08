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
            {
                await Results.Ok(new { status = "ready" }).ExecuteAsync(context);
                return;
            }

            await OperationalErrors.WriteProblemAsync(context, 503,
                result == DatabaseReadinessResult.SchemaNotReady
                    ? "DATABASE_SCHEMA_NOT_READY" : "DATABASE_UNAVAILABLE", context.RequestAborted);
        }).AllowAnonymous();
        return endpoints;
    }
}
