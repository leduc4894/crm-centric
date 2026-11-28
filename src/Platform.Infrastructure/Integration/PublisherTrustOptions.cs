using System.Collections.Immutable;

namespace Platform.Infrastructure.Integration;

internal enum PublisherKeyStatus { Unknown, Active, Revoked }

internal sealed record PublisherTrustEntry(string ModuleId, string KeyId, ImmutableArray<byte> PublicKey,
    ImmutableArray<Uri> AllowedOrigins, PublisherKeyStatus Status);

internal sealed record EventNamespaceAssignment(string ModuleId, string Prefix);

internal sealed class PublisherTrustSnapshot
{
    public long Revision { get; }
    public ImmutableArray<PublisherTrustEntry> Entries { get; }
    public ImmutableDictionary<string, string> EventNamespaces { get; }

    public PublisherTrustSnapshot(long revision, ImmutableArray<PublisherTrustEntry> entries,
        IEnumerable<EventNamespaceAssignment>? assignments = null)
    {
        var owners = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var assignment in assignments ?? [])
        {
            var prefix = assignment.Prefix;
            if (!System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\A[a-z][a-zA-Z0-9-]*\.(?:[a-zA-Z0-9-]+\.)*\z") ||
                prefix.StartsWith("crm.", StringComparison.Ordinal) ||
                owners.Any(owner => owner.Key == assignment.ModuleId || owner.Value.StartsWith(prefix, StringComparison.Ordinal) ||
                    prefix.StartsWith(owner.Value, StringComparison.Ordinal)))
                throw new ManifestValidationException(ManifestFailure.NamespaceConflict);
            owners.Add(assignment.ModuleId, prefix);
        }
        Revision = revision;
        Entries = entries;
        EventNamespaces = owners.ToImmutable();
    }
}
