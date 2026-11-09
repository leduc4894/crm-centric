using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Platform.Infrastructure.Observability;

public static class ProcessDiagnostic
{
    // A process result is one JSON root, with the original status/reason fields at the root.
    public static void Write(TextWriter writer, string service, string eventName, string correlationId,
        string? reasonToken = null, string? status = null)
    {
        var state = new Dictionary<string, object?>
        {
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["LogLevel"] = reasonToken is null ? "Information" : "Error",
            ["service"] = service,
            ["eventName"] = eventName,
            ["correlationId"] = correlationId
        };
        if (reasonToken is not null) state["reasonToken"] = reasonToken;
        if (status is not null) state["status"] = status;
        if (Activity.Current is { } activity)
        {
            state["traceId"] = activity.TraceId.ToHexString();
            state["spanId"] = activity.SpanId.ToHexString();
        }
        writer.WriteLine(JsonSerializer.Serialize(state));
    }
}
