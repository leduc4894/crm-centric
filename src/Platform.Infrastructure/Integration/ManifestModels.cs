using System.Collections.Immutable;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Integration")]

namespace Platform.Infrastructure.Integration;

internal enum FetchFailure { InvalidTarget, OriginDenied, AddressDenied, HttpStatus, TooLarge, Timeout, Transport }

internal sealed class ManifestFetchException(FetchFailure reason) : Exception(reason.ToString())
{
    public FetchFailure Reason { get; } = reason;
}

internal sealed record FetchResult(ImmutableArray<byte> Bytes, Uri FinalUri);

internal sealed class FetchPolicy
{
    private readonly ImmutableHashSet<string> origins;
    private readonly ImmutableArray<IPNetwork> privateNetworks;
    private static readonly IPNetwork[] Private = Networks("10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7");
    private static readonly IPNetwork[] Reserved = Networks(
        "0.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "168.63.129.16/32",
        "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "198.18.0.0/15", "198.51.100.0/24",
        "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4", "fd00:ec2::254/128", "fd20:ce::254/128", "2001::/23", "2001:db8::/32", "2002::/16", "3fff::/20");
    private static readonly IPNetwork GlobalV6 = IPNetwork.Parse("2000::/3"u8);

    public FetchPolicy(Uri baseOrigin, IEnumerable<Uri>? approvedOrigins = null, IEnumerable<IPNetwork>? operatorPrivateNetworks = null)
    {
        origins = (approvedOrigins ?? []).Prepend(baseOrigin).Select(Origin).ToImmutableHashSet(StringComparer.Ordinal);
        privateNetworks = (operatorPrivateNetworks ?? []).Select(n => IPNetwork.Parse(n.ToString())).ToImmutableArray();
    }

    public void Validate(Uri uri)
    {
        if (!origins.Contains(Origin(uri))) throw new ManifestFetchException(FetchFailure.OriginDenied);
    }

    internal static string Origin(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.OriginalString.Contains('\\') || HasUserInfoDelimiter(uri))
            throw new ManifestFetchException(FetchFailure.InvalidTarget);
        return uri.IdnHost.ToLowerInvariant() + ":" + uri.Port;
    }

    private static bool HasUserInfoDelimiter(Uri uri)
    {
        // Uri.UserInfo normalizes empty credentials to an empty string.
        var input = uri.OriginalString.AsSpan().Trim();
        int start = input.IndexOf("://", StringComparison.Ordinal);
        if (start < 0) return true;
        var authority = input[(start + 3)..];
        int end = authority.IndexOfAny('/', '?', '#');
        return (end < 0 ? authority : authority[..end]).Contains('@');
    }

    public bool Allows(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (Reserved.Any(n => n.Contains(address))) return false;
        if (Private.Any(n => n.Contains(address))) return privateNetworks.Any(n => n.Contains(address));
        return address.AddressFamily == AddressFamily.InterNetwork || GlobalV6.Contains(address);
    }

    private static IPNetwork[] Networks(params string[] values) => values.Select(IPNetwork.Parse).ToArray();
}

internal sealed record VerifiedPublisher(string ModuleId, string KeyId, long TrustRevision, string ManifestHash);

internal enum PublisherFailure { InvalidManifest, InvalidSidecar, UntrustedKey, OriginDenied, InvalidSignature }

internal sealed class PublisherVerificationException(PublisherFailure reason) : Exception(reason.ToString())
{
    public PublisherFailure Reason { get; } = reason;
}
