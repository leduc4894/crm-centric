using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Platform.Infrastructure.Integration;

internal sealed class SchemaSnapshotLoader(Func<Uri, FetchPolicy, CancellationToken, Task<FetchResult>> fetch,
    int indexBytesPerDocument = PinnedSchemaGraph.DefaultBytesPerDocument)
{
    public SchemaSnapshotLoader(SafeFetchClient client) : this(client.FetchSchemaAsync) { }

    public async Task<IReadOnlyList<SchemaSnapshot>> LoadAsync(ValidatedManifest manifest, FetchPolicy policy, CancellationToken ct)
    {
        policy = policy.WithBaseOrigin(manifest.BaseUrl);
        foreach (var reference in manifest.SchemaReferences) policy.Validate(reference.Uri);
        var snapshots = ImmutableArray.CreateBuilder<SchemaSnapshot>();
        var documents = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var reference in manifest.SchemaReferences)
        {
            ct.ThrowIfCancellationRequested();
            var result = await fetch(reference.Uri, policy, ct);
            if (result.FinalUri != reference.Uri) throw new ManifestFetchException(FetchFailure.InvalidTarget);
            if (result.Bytes.Length > SafeFetchClient.SchemaLimit) throw new ManifestValidationException(ManifestFailure.SchemaSize);
            if (Convert.ToHexStringLower(SHA256.HashData(result.Bytes.AsSpan())) != reference.Sha256)
                throw new ManifestValidationException(ManifestFailure.SchemaHash);
            if (documents.TryGetValue(reference.Uri.AbsoluteUri, out var previous) && !previous.Span.SequenceEqual(result.Bytes.AsSpan()))
                throw new ManifestValidationException(ManifestFailure.InvalidSchema);
            documents[reference.Uri.AbsoluteUri] = result.Bytes.AsMemory();
            snapshots.Add(new(reference, result.Bytes));
        }
        PinnedSchemaGraph.Validate(documents, indexBytesPerDocument);
        return snapshots.ToImmutable();
    }
}
