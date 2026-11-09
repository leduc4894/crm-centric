using System.Diagnostics;
using Platform.Infrastructure.Observability;

namespace Platform.Api.Operations;

public static class OperationalErrors
{
    public static Task WriteProblemAsync(HttpContext context, int status, string reasonToken, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.OperationalErrors");
        using (PlatformLogging.BeginOperation(logger, "crm-api", "dependency_failed", context.TraceIdentifier))
            logger.LogError("Operational failure {reasonToken} {correlationId}", reasonToken, context.TraceIdentifier);
        return context.Response.WriteAsJsonAsync(new
        {
            type = "urn:crm:problem:" + reasonToken.ToLowerInvariant().Replace('_', '-'),
            title = status == 500 ? "Internal server error" : "Dependency unavailable",
            status,
            reasonToken,
            correlationId = context.TraceIdentifier
        }, options: null, contentType: "application/problem+json", cancellationToken: cancellationToken);
    }

    public static WebApplication UseOperationalErrors(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.TraceIdentifier = Guid.NewGuid().ToString("N");
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
            using var activity = Activity.Current is null ? new Activity("crm-api.request").SetIdFormat(ActivityIdFormat.W3C).Start() : null;
            try { await next(context); }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { throw; }
            catch (Exception) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                await WriteProblemAsync(context, 500, "INTERNAL_ERROR", context.RequestAborted);
            }
        });
        return app;
    }
}
