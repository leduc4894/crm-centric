namespace Platform.Api.Operations;

public static class OperationalErrors
{
    public static Task WriteProblemAsync(HttpContext context, int status, string reasonToken, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Platform.OperationalErrors")
            .LogError("Operational failure {ReasonToken} {CorrelationId}", reasonToken, context.TraceIdentifier);
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
