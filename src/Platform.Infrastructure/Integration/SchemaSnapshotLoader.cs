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
            ValidateSchema(result.Bytes);
            snapshots.Add(new(reference, result.Bytes));
        }
        return snapshots.ToImmutable();
    }

    private static void ValidateSchema(ImmutableArray<byte> bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes.AsMemory(), new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
            var root = doc.RootElement;
            var text = root.GetRawText();
            if (!MetaSchema.Value.Validate(text)) throw Invalid();
            // Traverse only schema positions. Property names and instance data in const/enum are opaque.
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var references = new Dictionary<string, string>(StringComparer.Ordinal);
            var anchors = new Dictionary<string, string>(StringComparer.Ordinal);
            Index(root, "#");
            var depths = new Dictionary<string, int>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in children.Keys) Depth(path, 1);
            // Compile untrusted schema bytes without the global canonical-URI cache.
            JsonSchemaEvaluator.Compile(bytes.AsMemory(), new JsonSchemaEvaluatorOptions
            {
                DefaultDialect = JsonSchemaDialect.Draft202012,
                DocumentResolver = null,
                FallbackDocumentResolver = null
            });

            void Index(JsonElement node, string path)
            {
                children[path] = [];
                if (node.ValueKind != JsonValueKind.Object) return;
                foreach (var p in node.EnumerateObject())
                {
                    string child = path + "/" + p.Name;
                    switch (p.Name)
                    {
                        case "$schema":
                            if (p.Value.GetString() != Dialect) throw Invalid();
                            break;
                        case "$id":
                            // Nested resources change fragment resolution and are outside the single-document profile.
                            if (path != "#") throw Invalid();
                            break;
                        case "$dynamicRef": case "$dynamicAnchor": throw Invalid();
                        case "$ref":
                            var target = p.Value.GetString()!;
                            if (!target.StartsWith('#')) throw Invalid();
                            references.Add(path, Uri.UnescapeDataString(target));
                            break;
                        case "$anchor":
                            if (!anchors.TryAdd("#" + p.Value.GetString(), path)) throw Invalid();
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
                    Index(schema, key);
                }
            }

            int Depth(string path, int level)
            {
                if (level > 8 || !children.ContainsKey(path) || !visiting.Add(path)) throw Invalid();
                if (!depths.TryGetValue(path, out var depth))
                {
                    depth = 1;
                    if (references.TryGetValue(path, out var target))
                        children[path].Add(anchors.GetValueOrDefault(target, target));
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
