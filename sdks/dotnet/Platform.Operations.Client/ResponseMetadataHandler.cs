// Versioned facade copied into each generated client package by generate.py.
using System.Net;

namespace Platform.Operations.Client;

/// <summary>Captures HTTP metadata around a generated client call without altering its DTOs.</summary>
public sealed class ResponseMetadataHandler : DelegatingHandler
{
    private readonly AsyncLocal<Capture?> current = new();

    public ResponseMetadataHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    /// <summary>Executes one generated call and returns its response plus HTTP metadata.</summary>
    public async ValueTask<CapturedResponse<T>> CaptureAsync<T>(Func<ValueTask<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var previous = current.Value;
        var capture = new Capture();
        current.Value = capture;
        try
        {
            T response = await operation().ConfigureAwait(false);
            if (capture.Headers is null)
                throw new InvalidOperationException("SDK_RESPONSE_METADATA_MISSING");
            return new(response, capture.StatusCode, capture.ContentType, capture.Headers);
        }
        finally { current.Value = previous; }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var capture = current.Value;
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (capture is not null)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in response.Headers) headers[header.Key] = header.Value.ToArray();
            if (response.Content is not null)
                foreach (var header in response.Content.Headers) headers[header.Key] = header.Value.ToArray();
            capture.StatusCode = response.StatusCode;
            capture.ContentType = response.Content?.Headers.ContentType?.MediaType;
            capture.Headers = headers;
        }
        return response;
    }

    private sealed class Capture
    {
        public HttpStatusCode StatusCode { get; set; }
        public string? ContentType { get; set; }
        public IReadOnlyDictionary<string, string[]>? Headers { get; set; }
    }
}

/// <summary>A generated response and HTTP status, media type and response headers.</summary>
public sealed record CapturedResponse<T>(T Response, HttpStatusCode StatusCode, string? ContentType, IReadOnlyDictionary<string, string[]> Headers);
