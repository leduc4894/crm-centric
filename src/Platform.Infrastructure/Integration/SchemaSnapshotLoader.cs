using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Corvus.Text.Json.Validator;
using Corvus.Text.Json.RuntimeEvaluator;

namespace Platform.Infrastructure.Integration;

internal sealed class SchemaSnapshotLoader(Func<Uri, FetchPolicy, CancellationToken, Task<FetchResult>> fetch)
{
    public SchemaSnapshotLoader(SafeFetchClient client) : this(client.FetchSchemaAsync) { }

    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";
    private static readonly Lazy<JsonSchema> MetaSchema = new(() => JsonSchema.FromUri(Dialect, options: ManifestValidator.Offline));

    public async Task<IReadOnlyList<SchemaSnapshot>> LoadAsync(ValidatedManifest manifest, FetchPolicy policy, CancellationToken ct)
    {
        policy = policy.WithBaseOrigin(manifest.BaseUrl);
        foreach (var reference in manifest.SchemaReferences) policy.Validate(reference.Uri);
        var snapshots = ImmutableArray.CreateBuilder<SchemaSnapshot>();
        foreach (var reference in manifest.SchemaReferences)
        {
            ct.ThrowIfCancellationRequested();
            var result = await fetch(reference.Uri, policy, ct);
            if (result.FinalUri != reference.Uri) throw new ManifestFetchException(FetchFailure.InvalidTarget);
            if (result.Bytes.Length > SafeFetchClient.SchemaLimit) throw new ManifestValidationException(ManifestFailure.SchemaSize);
            if (Convert.ToHexStringLower(SHA256.HashData(result.Bytes.AsSpan())) != reference.Sha256)
                throw new ManifestValidationException(ManifestFailure.SchemaHash);
            ValidateSchema(result.Bytes, reference.Uri);
            snapshots.Add(new(reference, result.Bytes));
        }
        return snapshots.ToImmutable();
    }

    private static void ValidateSchema(ImmutableArray<byte> bytes, Uri retrievalUri)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes.AsMemory(), new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
            var root = doc.RootElement;
            var text = root.GetRawText();
            if (!MetaSchema.Value.Validate(text)) throw Invalid();
            // Traverse only schema positions. Property names and instance data in const/enum are opaque.
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var resources = new Dictionary<string, string>(StringComparer.Ordinal) { [retrievalUri.AbsoluteUri] = "#" };
            var references = new List<(string Path, Uri Target, bool Dynamic)>();
            var anchors = new Dictionary<(string Resource, string Name), (string Path, bool Dynamic)>();
            Index(root, "#", retrievalUri);
            foreach (var (path, target, isDynamic) in references)
            {
                var resource = target.AbsoluteUri.Split('#')[0];
                if (!resources.TryGetValue(resource, out var resourcePath)) throw Invalid();
                var fragment = Uri.UnescapeDataString(target.Fragment.Length == 0 ? "" : target.Fragment[1..]);
                if (fragment.Length == 0 || fragment.StartsWith('/'))
                    children[path].Add(resourcePath + fragment);
                else
                {
                    if (!anchors.TryGetValue((resourcePath, fragment), out var anchor)) throw Invalid();
                    children[path].Add(anchor.Path);
                    // A dynamic reference can select an outer resource's dynamic anchor.
                    if (isDynamic && anchor.Dynamic)
                        foreach (var candidate in anchors.Where(a => a.Key.Name == fragment && a.Value.Dynamic))
                            children[path].Add(candidate.Value.Path);
                }
            }
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in children.Keys) Depth(path, 1);
            // Compile untrusted schema bytes without the global canonical-URI cache.
            JsonSchemaEvaluator.Compile(bytes.AsMemory(), new JsonSchemaEvaluatorOptions
            {
                BaseUri = retrievalUri.AbsoluteUri,
                DefaultDialect = JsonSchemaDialect.Draft202012,
                DocumentResolver = null,
                FallbackDocumentResolver = null
            });

            void Index(JsonElement node, string path, Uri resource)
            {
                children[path] = [];
                if (node.ValueKind != JsonValueKind.Object) return;
                if (node.TryGetProperty("$id"u8, out var id))
                {
                    resource = new Uri(resource, id.GetString()!);
                    if (resource.Fragment.Length > 0 || (resources.TryGetValue(resource.AbsoluteUri, out var existing) && existing != path))
                        throw Invalid();
                    resources[resource.AbsoluteUri] = path;
                }
                foreach (var p in node.EnumerateObject())
                {
                    string child = path + "/" + p.Name;
                    switch (p.Name)
                    {
                        case "$schema":
                            if (p.Value.GetString() != Dialect) throw Invalid();
                            break;
                        case "$ref": case "$dynamicRef":
                            references.Add((path, new Uri(resource, p.Value.GetString()!), p.Name == "$dynamicRef"));
                            break;
                        case "$anchor": case "$dynamicAnchor":
                            if (!anchors.TryAdd((resources[resource.AbsoluteUri], p.Value.GetString()!), (path, p.Name == "$dynamicAnchor")))
                                throw Invalid();
                            break;
                        case "properties": case "patternProperties": case "dependentSchemas": case "$defs": case "definitions":
                            foreach (var member in p.Value.EnumerateObject())
                                Add(member.Value, child + "/" + member.Name.Replace("~", "~0").Replace("/", "~1"), p.Name is not ("$defs" or "definitions"));
                            break;
                        case "allOf": case "anyOf": case "oneOf": case "prefixItems":
                            int index = 0;
                            foreach (var item in p.Value.EnumerateArray()) Add(item, child + "/" + index++, true);
                            break;
                        case "items": case "contains": case "additionalProperties": case "propertyNames":
                        case "if": case "then": case "else": case "not": case "unevaluatedProperties":
                        case "unevaluatedItems": case "contentSchema":
                            Add(p.Value, child, true);
                            break;
                    }
                }
                void Add(JsonElement schema, string key, bool evaluated)
                {
                    if (evaluated) children[path].Add(key);
                    Index(schema, key, resource);
                }
            }

            int Depth(string path, int level)
            {
                if (level > 8 || !children.ContainsKey(path) || !visiting.Add(path)) throw Invalid();
                if (!depths.TryGetValue(path, out var depth))
                {
                    depth = 1;
                    foreach (var child in children[path]) depth = Math.Max(depth, 1 + Depth(child, level + 1));
                    depths.Add(path, depth);
                }
                visiting.Remove(path);
                if (level + depth - 1 > 8) throw Invalid();
                return depth;
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            throw Invalid();
        }
    }

    private static ManifestValidationException Invalid() => new(ManifestFailure.InvalidSchema);
}
