using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Corvus.Text.Json.Validator;

namespace Platform.Infrastructure.Integration;

internal enum ManifestFailure { InvalidManifest, InvalidDescriptor, NamespaceConflict, NamespaceDenied, OriginDenied, InvalidSchema, SchemaHash, SchemaSize }
internal sealed class ManifestValidationException(ManifestFailure reason) : Exception(reason.ToString())
{
    public ManifestFailure Reason { get; } = reason;
}

internal sealed record SchemaReference(Uri Uri, string Version, string Sha256);
internal sealed record SchemaSnapshot(SchemaReference Reference, ImmutableArray<byte> Bytes);
internal sealed record ValidatedManifest(VerifiedPublisher Publisher, string Version, Uri BaseUrl,
    ImmutableArray<byte> Bytes, JsonElement Document, ImmutableArray<SchemaReference> SchemaReferences, ImmutableArray<Uri> RequestedOrigins);

internal static class ManifestValidator
{
    internal static readonly JsonSchema.Options Offline = new(allowFileSystemAndHttpResolution: false);
    private static readonly Lazy<JsonSchema> Contract = new(() =>
    {
        using var stream = typeof(ManifestValidator).Assembly.GetManifestResourceStream("module-manifest.schema.json")!;
        return JsonSchema.FromStream(stream, "urn:crm:module-manifest:v2", options: Offline);
    });

    public static ValidatedManifest Validate(ReadOnlyMemory<byte> manifest, VerifiedPublisher publisher)
    {
        try
        {
            var bytes = ImmutableArray.Create(manifest.Span);
            using var doc = JsonDocument.Parse(bytes.AsMemory(), new JsonDocumentOptions { AllowDuplicateProperties = false });
            var root = doc.RootElement;
            if (Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())) != publisher.ManifestHash ||
                !Contract.Value.Validate(root.GetRawText()) || String(root, "moduleId") != publisher.ModuleId)
                throw new ManifestValidationException(ManifestFailure.InvalidManifest);
            var baseUrl = Target(root, "baseUrl");
            string origin = FetchPolicy.Origin(baseUrl);
            if (!publisher.AllowedOrigins.Any(u => FetchPolicy.Origin(u) == origin))
                throw new ManifestValidationException(ManifestFailure.OriginDenied);
            var permissions = root.GetProperty("permissions"u8).EnumerateArray().Select(p => p.GetString()).ToHashSet(StringComparer.Ordinal);
            var schemas = ImmutableArray.CreateBuilder<SchemaReference>();
            var requested = new HashSet<Uri>();
            void Record(Uri target)
            {
                if (FetchPolicy.Origin(target) != origin)
                    requested.Add(new Uri(target.GetLeftPart(UriPartial.Authority)));
            }
            foreach (var field in new[] { "navigationUrl", "healthUrl" })
                if (root.TryGetProperty(field, out _)) Record(Target(root, field));
            foreach (var kind in new[] { "events", "actions", "panels" })
            {
                if (!root.TryGetProperty(kind, out var descriptors)) continue;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var descriptor in descriptors.EnumerateArray())
                {
                    var id = String(descriptor, kind == "events" ? "type" : "id");
                    if (!ids.Add(id) || descriptor.GetProperty("requiredScopes"u8).EnumerateArray().Any(s => !permissions.Contains(s.GetString())))
                        throw new ManifestValidationException(ManifestFailure.InvalidDescriptor);
                    if (kind == "events")
                    {
                        var prefix = publisher.EventNamespace;
                        if (prefix is null || !id.StartsWith(prefix, StringComparison.Ordinal) ||
                            !id[prefix.Length..].Contains('.') || id.Contains("..", StringComparison.Ordinal))
                            throw new ManifestValidationException(ManifestFailure.NamespaceDenied);
                    }
                    else Record(Target(descriptor, "endpoint"));
                    foreach (var name in new[] { "schema", "inputSchema", "outputSchema" })
                    {
                        if (!descriptor.TryGetProperty(name, out var schema)) continue;
                        var uri = Target(schema, "uri");
                        Record(uri);
                        schemas.Add(new(uri, String(schema, "version"), String(schema, "sha256")));
                    }
                }
            }
            return new(publisher, String(root, "version"), baseUrl, bytes, root.Clone(), [.. schemas.Distinct()], [.. requested]);
        }
        catch (InvalidOperationException) { throw new ManifestValidationException(ManifestFailure.InvalidManifest); }
        catch (JsonException) { throw new ManifestValidationException(ManifestFailure.InvalidManifest); }
        catch (ManifestFetchException) { throw new ManifestValidationException(ManifestFailure.OriginDenied); }
    }

    private static string String(JsonElement value, string field) => value.GetProperty(field).GetString()!;
    private static Uri Target(JsonElement value, string field)
    {
        if (!Uri.TryCreate(String(value, field), UriKind.Absolute, out var uri))
            throw new ManifestValidationException(ManifestFailure.OriginDenied);
        FetchPolicy.Origin(uri);
        return uri;
    }
}
