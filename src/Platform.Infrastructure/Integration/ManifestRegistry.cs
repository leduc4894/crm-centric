using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace Platform.Infrastructure.Integration;

internal enum RegistryResult { Inserted, AlreadyRegistered }
internal enum RegistryFailure { InvalidSnapshot, VersionConflict, Downgrade, UnsupportedIsolation, Storage }

internal sealed class RegistryException(RegistryFailure reason, Guid correlationId) : Exception(reason.ToString())
{
    public RegistryFailure Reason { get; } = reason;
    public Guid CorrelationId { get; } = correlationId;
}

// Trust is operator-managed. Every stored row is historical verification evidence.
// Installation must obtain current publisher trust and verify the release before use.
internal sealed class ManifestRegistry(NpgsqlDataSource dataSource)
{
    public async Task<RegistryResult> RegisterAsync(ValidatedManifest manifest, IReadOnlyList<SchemaSnapshot> snapshots,
        Guid correlationId, CancellationToken ct)
    {
        // Reconstruct signed metadata from exact bytes so every persisted reference,
        // permission and origin comes from the same manifest as the verification proof.
        ValidatedManifest candidate;
        try { candidate = ManifestValidator.Validate(manifest.Bytes.AsMemory(), manifest.Publisher); }
        catch (ManifestValidationException) { throw new RegistryException(RegistryFailure.InvalidSnapshot, correlationId); }

        var schemas = snapshots.ToArray();
        if (schemas.Length != candidate.SchemaReferences.Length || schemas.Where((s, i) =>
            s.Reference != candidate.SchemaReferences[i] ||
            Convert.ToHexStringLower(SHA256.HashData(s.Bytes.AsSpan())) != s.Reference.Sha256).Any())
            throw new RegistryException(RegistryFailure.InvalidSnapshot, correlationId);

        await using var cmd = dataSource.CreateCommand("""
            SELECT platform.register_manifest($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)
            """);
        cmd.Parameters.AddWithValue(candidate.Bytes.ToArray());
        cmd.Parameters.AddWithValue(candidate.Publisher.KeyId);
        cmd.Parameters.AddWithValue(candidate.Publisher.TrustRevision);
        cmd.Parameters.AddWithValue(candidate.RequestedOrigins.Select(o => o.AbsoluteUri).ToArray());
        cmd.Parameters.AddWithValue(schemas.Select(s => s.Reference.Uri.AbsoluteUri).ToArray());
        cmd.Parameters.AddWithValue(schemas.Select(s => s.Reference.Version).ToArray());
        cmd.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Bytea, schemas.Select(s => s.Bytes.ToArray()).ToArray());
        cmd.Parameters.AddWithValue(candidate.Publisher.ModuleId);
        cmd.Parameters.AddWithValue(candidate.Version);
        cmd.Parameters.AddWithValue(candidate.Document.GetProperty("permissions"u8).EnumerateArray().Select(p => p.GetString()!).ToArray());
        var descriptors = new List<byte[]>();
        foreach (var kind in new[] { "events", "actions", "panels" })
            if (candidate.Document.TryGetProperty(kind, out var list))
                descriptors.AddRange(list.EnumerateArray().Select(d => Encoding.UTF8.GetBytes(d.GetRawText())));
        cmd.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Bytea, descriptors.ToArray());
        try
        {
            return await cmd.ExecuteScalarAsync(ct) switch
            {
                "inserted" => RegistryResult.Inserted,
                "already_registered" => RegistryResult.AlreadyRegistered,
                "version_conflict" => throw new RegistryException(RegistryFailure.VersionConflict, correlationId),
                "unsupported_isolation" => throw new RegistryException(RegistryFailure.UnsupportedIsolation, correlationId),
                "downgrade" => throw new RegistryException(RegistryFailure.Downgrade, correlationId),
                _ => throw new RegistryException(RegistryFailure.Storage, correlationId)
            };
        }
        catch (NpgsqlException) { throw new RegistryException(RegistryFailure.Storage, correlationId); }
        catch (TimeoutException) { throw new RegistryException(RegistryFailure.Storage, correlationId); }
    }
}
