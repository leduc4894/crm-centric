using System.Buffers.Text;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using NSec.Cryptography;

namespace Platform.Infrastructure.Integration;

internal static class PublisherVerifier
{
    public static VerifiedPublisher Verify(FetchResult manifest, FetchResult sidecar, PublisherTrustSnapshot trust)
    {
        using var body = Parse(manifest.Bytes, PublisherFailure.InvalidManifest);
        string moduleId = Field(body.RootElement, "moduleId", PublisherFailure.InvalidManifest);
        using var envelope = Parse(sidecar.Bytes, PublisherFailure.InvalidSidecar);
        var root = envelope.RootElement;
        if (root.GetPropertyCount() != 3 || Field(root, "algorithm", PublisherFailure.InvalidSidecar) != "Ed25519")
            throw new PublisherVerificationException(PublisherFailure.InvalidSidecar);
        string keyId = Field(root, "keyId", PublisherFailure.InvalidSidecar);
        if (keyId.Length > 64 || !keyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            throw new PublisherVerificationException(PublisherFailure.InvalidSidecar);
        byte[] signature = DecodeSignature(Field(root, "signature", PublisherFailure.InvalidSidecar));

        var matches = trust.Entries.Where(e => e.ModuleId == moduleId && e.KeyId == keyId).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].Status != PublisherKeyStatus.Active || matches[0].PublicKey.Length != 32)
            throw new PublisherVerificationException(PublisherFailure.UntrustedKey);
        var entry = matches[0];
        try
        {
            string origin = FetchPolicy.Origin(manifest.FinalUri);
            if (FetchPolicy.Origin(sidecar.FinalUri) != origin || !entry.AllowedOrigins.Any(u => FetchPolicy.Origin(u) == origin))
                throw new PublisherVerificationException(PublisherFailure.OriginDenied);
        }
        catch (ManifestFetchException)
        {
            throw new PublisherVerificationException(PublisherFailure.OriginDenied);
        }

        var algorithm = SignatureAlgorithm.Ed25519;
        if (!PublicKey.TryImport(algorithm, entry.PublicKey.AsSpan(), KeyBlobFormat.RawPublicKey, out var key) ||
            !algorithm.Verify(key, manifest.Bytes.AsSpan(), signature))
            throw new PublisherVerificationException(PublisherFailure.InvalidSignature);
        return new(moduleId, keyId, trust.Revision, Convert.ToHexStringLower(SHA256.HashData(manifest.Bytes.AsSpan())));
    }

    private static JsonDocument Parse(ImmutableArray<byte> bytes, PublisherFailure failure)
    {
        try
        {
            var document = JsonDocument.Parse(bytes.AsMemory(), new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException)
        {
            throw new PublisherVerificationException(failure);
        }
        throw new PublisherVerificationException(failure);
    }

    private static string Field(JsonElement root, string name, PublisherFailure failure)
    {
        try
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
                value.GetString() is { Length: > 0 } text) return text;
        }
        catch (InvalidOperationException)
        {
            // System.Text.Json decodes escaped UTF-16 when reading the property.
            throw new PublisherVerificationException(failure);
        }
        throw new PublisherVerificationException(failure);
    }

    private static byte[] DecodeSignature(string encoded)
    {
        byte[] bytes = new byte[64];
        if (encoded.Length != 86 || !Base64Url.IsValid(encoded) || !Base64Url.TryDecodeFromChars(encoded, bytes, out int written) ||
            written != bytes.Length || Base64Url.EncodeToString(bytes) != encoded)
            throw new PublisherVerificationException(PublisherFailure.InvalidSidecar);
        return bytes;
    }
}
