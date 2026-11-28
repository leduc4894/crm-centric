using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Platform.Infrastructure.Integration;

internal sealed class SafeFetchClient
{
    internal const int ManifestLimit = 256 * 1024;
    internal const int SidecarLimit = 4 * 1024;
    internal const int SchemaLimit = 64 * 1024;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve;
    private readonly X509Certificate2? trustRoot;

    public SafeFetchClient() : this(Dns.GetHostAddressesAsync) { }

    internal SafeFetchClient(Func<string, CancellationToken, Task<IPAddress[]>> resolve, X509Certificate2? trustRoot = null)
    {
        this.resolve = resolve;
        this.trustRoot = trustRoot;
    }

    public Task<FetchResult> FetchAsync(Uri uri, FetchPolicy policy, int maxBytes, CancellationToken ct)
    {
        policy.Validate(uri);
        if (uri.Query.Length != 0) throw new ManifestFetchException(FetchFailure.InvalidTarget);
        return FetchValidatedAsync(uri, policy, maxBytes, ct);
    }

    public Task<FetchResult> FetchSchemaAsync(Uri uri, FetchPolicy policy, CancellationToken ct)
    {
        policy.Validate(uri);
        return FetchValidatedAsync(uri, policy, SchemaLimit, ct);
    }

    private async Task<FetchResult> FetchValidatedAsync(Uri uri, FetchPolicy policy, int maxBytes, CancellationToken ct)
    {
        if (maxBytes is <= 0 or > ManifestLimit) throw new ManifestFetchException(FetchFailure.TooLarge);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            MaxResponseHeadersLength = 16,
            ConnectCallback = (context, token) => ConnectAsync(context.DnsEndPoint, policy, token),
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = new X509ChainPolicy
                {
                    DisableCertificateDownloads = true,
                    ApplicationPolicy = { new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1") },
                    RevocationMode = X509RevocationMode.Offline,
                    VerificationFlags = X509VerificationFlags.IgnoreEndRevocationUnknown |
                        X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown |
                        X509VerificationFlags.IgnoreRootRevocationUnknown
                }
            }
        };
        if (trustRoot != null)
        {
            handler.SslOptions.CertificateChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            handler.SslOptions.CertificateChainPolicy.CustomTrustStore.Add(trustRoot);
        }
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) throw new ManifestFetchException(FetchFailure.HttpStatus);
            if (response.Content.Headers.ContentLength > maxBytes) throw new ManifestFetchException(FetchFailure.TooLarge);
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var bytes = new MemoryStream();
            byte[] chunk = new byte[Math.Min(8192, maxBytes + 1)];
            while (true)
            {
                int count = await stream.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, maxBytes - (int)bytes.Length + 1)), deadline.Token);
                if (count == 0) break;
                if (bytes.Length + count > maxBytes) throw new ManifestFetchException(FetchFailure.TooLarge);
                bytes.Write(chunk, 0, count);
            }
            return new FetchResult(ImmutableArray.Create(bytes.GetBuffer(), 0, (int)bytes.Length), uri);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ManifestFetchException(FetchFailure.Timeout);
        }
        catch (HttpRequestException e) when (e.InnerException is ManifestFetchException failure)
        {
            throw new ManifestFetchException(failure.Reason);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw new ManifestFetchException(FetchFailure.Transport);
        }
    }

    private async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint, FetchPolicy policy, CancellationToken ct)
    {
        IPAddress[] answers = IPAddress.TryParse(endpoint.Host, out var literal) ? [literal] : await resolve(endpoint.Host, ct);
        if (answers.Length == 0 || answers.Any(address => !policy.Allows(address)))
            throw new ManifestFetchException(FetchFailure.AddressDenied);
        var address = answers[0];
        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
