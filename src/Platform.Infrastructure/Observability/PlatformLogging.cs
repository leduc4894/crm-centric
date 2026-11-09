using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Platform.Infrastructure.Observability;

public static class PlatformLogging
{
    public static void Configure(ILoggingBuilder logging, string service)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        });
        // Request-start/finish messages include the caller's raw query string.
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        // Provider diagnostics can expose connection metadata even without sensitive-data logging.
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        logging.AddFilter("Npgsql", LogLevel.None);
    }

    public static IDisposable? BeginOperation(ILogger logger, string service, string eventName, string correlationId)
    {
        var state = new Dictionary<string, object?>
        {
            ["service"] = service,
            ["eventName"] = eventName,
            ["correlationId"] = correlationId
        };
        if (Activity.Current is { } activity)
        {
            state["traceId"] = activity.TraceId.ToHexString();
            state["spanId"] = activity.SpanId.ToHexString();
        }
        return logger.BeginScope(state);
    }
}
