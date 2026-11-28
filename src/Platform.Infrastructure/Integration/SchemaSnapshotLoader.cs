using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvus.Text.Json.Validator;
using Corvus.Text.Json.RuntimeEvaluator;

namespace Platform.Infrastructure.Integration;

internal sealed class SchemaSnapshotLoader(Func<Uri, FetchPolicy, CancellationToken, Task<FetchResult>> fetch)
{
    public SchemaSnapshotLoader(SafeFetchClient client) : this(client.FetchSchemaAsync) { }

    private sealed record DynamicScope(string Resource, DynamicScope? Outer);
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";
    private static readonly Lazy<JsonSchema> MetaSchema = new(() => JsonSchema.FromUri(Dialect, options: ManifestValidator.Offline));

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
                throw Invalid();
            documents[reference.Uri.AbsoluteUri] = result.Bytes.AsMemory();
            snapshots.Add(new(reference, result.Bytes));
        }
        ValidateSchemas(documents);
        return snapshots.ToImmutable();
    }

    private static void ValidateSchemas(Dictionary<string, ReadOnlyMemory<byte>> documents)
    {
        try
        {
            // Traverse only schema positions. Property names and instance data in const/enum are opaque.
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var resources = documents.Keys.ToDictionary(uri => uri, uri => uri + "#", StringComparer.Ordinal);
            var resolverBytes = new Dictionary<string, ReadOnlyMemory<byte>>(documents, StringComparer.Ordinal);
            var references = new List<(string Path, Uri Target, bool Dynamic)>();
            var anchors = new Dictionary<(string Resource, string Name), (string Path, bool Dynamic)>();
            var owningResources = new Dictionary<string, string>(StringComparer.Ordinal);
            var dynamicReferences = new Dictionary<string, (string Name, string Fallback)>(StringComparer.Ordinal);
            foreach (var (uri, bytes) in documents)
            {
                using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
                if (!MetaSchema.Value.Validate(doc.RootElement.GetRawText())) throw Invalid();
                Index(doc.RootElement, resources[uri], new Uri(uri));
            }
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
                    if (isDynamic && anchor.Dynamic)
                        dynamicReferences.Add(path, (fragment, anchor.Path));
                    else
                        children[path].Add(anchor.Path);
                }
            }
            var dynamicResources = anchors.Where(a => a.Value.Dynamic).Select(a => a.Key.Resource).ToHashSet(StringComparer.Ordinal);
            var depths = new Dictionary<(string Path, DynamicScope? Scope), int>();
            var visiting = new HashSet<(string Path, DynamicScope? Scope)>();
            int remainingVisits = 8 * SafeFetchClient.SchemaLimit;
            foreach (var path in children.Keys) Depth(path, null, 1);
            // Compile against only this candidate's checked bytes. Canonical IDs never enter a global cache.
            foreach (var (uri, bytes) in documents)
            {
                JsonSchemaEvaluator.Compile(bytes, new JsonSchemaEvaluatorOptions
                {
                    BaseUri = uri,
                    DefaultDialect = JsonSchemaDialect.Draft202012,
                    DocumentResolver = resolverBytes.TryGetValue,
                    FallbackDocumentResolver = null
                });
            }

            void Index(JsonElement node, string path, Uri resource)
            {
                children[path] = [];
                owningResources[path] = resources[resource.AbsoluteUri];
                if (node.ValueKind != JsonValueKind.Object) return;
                if (node.TryGetProperty("$id"u8, out var id))
                {
                    resource = new Uri(resource, id.GetString()!);
                    if (resource.Fragment.Length > 0 || (resources.TryGetValue(resource.AbsoluteUri, out var existing) && existing != path))
                        throw Invalid();
                    resources[resource.AbsoluteUri] = path;
                    // When compiled as a peer, an embedded relative ID needs its original enclosing base.
                    var standalone = JsonNode.Parse(node.GetRawText())!;
                    standalone["$id"] = resource.AbsoluteUri;
                    resolverBytes[resource.AbsoluteUri] = JsonSerializer.SerializeToUtf8Bytes(standalone);
                    owningResources[path] = path;
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

            int Depth(string path, DynamicScope? scope, int level)
            {
                if (--remainingVisits < 0 || level > 8 || !owningResources.TryGetValue(path, out var resource)) throw Invalid();
                // The first occurrence of a resource wins during outermost-anchor lookup.
                if (dynamicResources.Contains(resource))
                {
                    var current = scope;
                    while (current != null && current.Resource != resource) current = current.Outer;
                    if (current == null) scope = new(resource, scope);
                }

                var state = (path, scope);
                if (!visiting.Add(state)) throw Invalid();
                if (!depths.TryGetValue(state, out var depth))
                {
                    depth = 1;
                    if (dynamicReferences.TryGetValue(path, out var reference))
                    {
                        var target = reference.Fallback;
                        for (var frame = scope; frame != null; frame = frame.Outer)
                            if (anchors.TryGetValue((frame.Resource, reference.Name), out var match) && match.Dynamic)
                                target = match.Path;
                        depth += Depth(target, scope, level + 1);
                    }
                    foreach (var child in children[path])
                        depth = Math.Max(depth, 1 + Depth(child, scope, level + 1));
                    depths.Add(state, depth);
                }
                visiting.Remove(state);
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
