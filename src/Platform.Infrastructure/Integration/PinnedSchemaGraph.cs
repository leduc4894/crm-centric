using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Corvus.Text.Json.RuntimeEvaluator;
using Corvus.Text.Json.Validator;

namespace Platform.Infrastructure.Integration;

internal sealed class PinnedSchemaGraph
{
    internal const int DefaultBytesPerDocument = 8 * 1024 * 1024;
    private const int MaxIndexBytes = 64 * 1024 * 1024;
    private const string Dialect = "https://json-schema.org/draft/2020-12/schema";
    private static readonly Lazy<JsonSchema> MetaSchema = new(() => JsonSchema.FromUri(Dialect, options: ManifestValidator.Offline));

    private sealed class Node(int resource)
    {
        public int Resource = resource;
        public List<int>? Children;
    }

    private sealed record DynamicScope(int Resource, DynamicScope? Outer);
    private readonly List<Node> nodes = [];
    private readonly Dictionary<(int Parent, string Keyword, string? Member), int> positions = [];
    private readonly Dictionary<string, int> resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadOnlyMemory<byte>> resolverBytes = new(StringComparer.Ordinal);
    private readonly List<(int Node, Uri Base, string Text, bool Dynamic)> references = [];
    private readonly Dictionary<(int Resource, string Name), (int Node, bool Dynamic)> anchors = [];
    private readonly Dictionary<int, (string Name, int Fallback)> dynamicReferences = [];
    private readonly HashSet<int> dynamicResources = [];
    private long remainingBytes;

    private PinnedSchemaGraph(int documentCount, int bytesPerDocument) =>
        remainingBytes = Math.Min(MaxIndexBytes, (long)documentCount * bytesPerDocument);

    public static void Validate(Dictionary<string, ReadOnlyMemory<byte>> documents, int bytesPerDocument)
    {
        try
        {
            var graph = new PinnedSchemaGraph(documents.Count, bytesPerDocument);
            foreach (var (uri, bytes) in documents)
            {
                graph.Reserve(128L + 2L * uri.Length);
                int root = graph.nodes.Count;
                graph.AddNode(root);
                graph.resources.Add(uri, root);
                graph.resolverBytes.Add(uri, bytes);
            }
            foreach (var (uri, bytes) in documents)
            {
                using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = 8 });
                if (!MetaSchema.Value.Validate(doc.RootElement.GetRawText())) throw Invalid();
                graph.Index(doc.RootElement, graph.resources[uri], new Uri(uri));
            }
            graph.ResolveReferences();
            graph.CheckDepth(documents.Count);
            foreach (var (uri, bytes) in documents)
            {
                JsonSchemaEvaluator.Compile(bytes, new JsonSchemaEvaluatorOptions
                {
                    BaseUri = uri,
                    DefaultDialect = JsonSchemaDialect.Draft202012,
                    DocumentResolver = graph.resolverBytes.TryGetValue,
                    FallbackDocumentResolver = null
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or FormatException)
        {
            throw Invalid();
        }
    }

    // Charge estimated retained storage before extending the index. Includes table/list capacity
    // and owned text, independently of the fetch limits and the traversal work limit.
    private void Reserve(long bytes)
    {
        if (bytes > remainingBytes) throw new ManifestValidationException(ManifestFailure.SchemaComplexity);
        remainingBytes -= bytes;
    }

    private int AddNode(int resource)
    {
        Reserve(64);
        int id = nodes.Count;
        nodes.Add(new(resource));
        return id;
    }

    private void AddEdge(int parent, int child)
    {
        var node = nodes[parent];
        Reserve(node.Children is null ? 80 : 16);
        (node.Children ??= []).Add(child);
    }

    private void Index(JsonElement schema, int id, Uri baseUri)
    {
        if (schema.ValueKind != JsonValueKind.Object) return;
        if (schema.TryGetProperty("$id"u8, out var identifier))
        {
            baseUri = new Uri(baseUri, identifier.GetString()!);
            string uri = baseUri.AbsoluteUri;
            if (baseUri.Fragment.Length > 0 || (resources.TryGetValue(uri, out int existing) && existing != id)) throw Invalid();
            Reserve(128L + 2L * uri.Length);
            resources[uri] = id;
            nodes[id].Resource = id;
            // The compiler may load this embedded resource independently of its enclosing base.
            var standalone = JsonNode.Parse(schema.GetRawText())!;
            standalone["$id"] = uri;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(standalone);
            Reserve(bytes.Length);
            resolverBytes[uri] = bytes;
        }
        foreach (var property in schema.EnumerateObject())
        {
            string keyword = property.Name;
            switch (keyword)
            {
                case "$schema":
                    if (property.Value.GetString() != Dialect) throw Invalid();
                    break;
                case "$ref": case "$dynamicRef":
                    string reference = property.Value.GetString()!;
                    Reserve(128L + 2L * reference.Length);
                    references.Add((id, baseUri, reference, keyword == "$dynamicRef"));
                    break;
                case "$anchor": case "$dynamicAnchor":
                    string name = property.Value.GetString()!;
                    Reserve(128L + 2L * name.Length);
                    bool dynamic = keyword == "$dynamicAnchor";
                    if (!anchors.TryAdd((nodes[id].Resource, name), (id, dynamic))) throw Invalid();
                    if (dynamic) dynamicResources.Add(nodes[id].Resource);
                    break;
                case "properties": case "patternProperties": case "dependentSchemas": case "$defs": case "definitions":
                    foreach (var member in property.Value.EnumerateObject())
                        Add(member.Value, keyword, member.Name.Replace("~", "~0").Replace("/", "~1"), keyword is not ("$defs" or "definitions"));
                    break;
                case "allOf": case "anyOf": case "oneOf": case "prefixItems":
                    int index = 0;
                    foreach (var item in property.Value.EnumerateArray())
                        Add(item, keyword, (index++).ToString(CultureInfo.InvariantCulture), true);
                    break;
                case "items": case "contains": case "additionalProperties": case "propertyNames":
                case "if": case "then": case "else": case "not": case "unevaluatedProperties":
                case "unevaluatedItems": case "contentSchema":
                    Add(property.Value, keyword, null, true);
                    break;
            }
        }

        void Add(JsonElement childSchema, string keyword, string? member, bool evaluated)
        {
            int child = AddNode(nodes[id].Resource);
            Reserve(96L + 2L * (keyword.Length + (member?.Length ?? 0)));
            positions.Add((id, keyword, member), child);
            if (evaluated) AddEdge(id, child);
            Index(childSchema, child, baseUri);
        }
    }

    private void ResolveReferences()
    {
        foreach (var (source, baseUri, text, isDynamic) in references)
        {
            var target = new Uri(baseUri, text);
            string resource = target.AbsoluteUri.Split('#', 2)[0];
            if (!resources.TryGetValue(resource, out int node)) throw Invalid();
            string fragment = target.Fragment.Length == 0 ? string.Empty : Uri.UnescapeDataString(target.Fragment[1..]);
            if (fragment.Length == 0 || fragment[0] == '/')
                AddEdge(source, ResolvePointer(node, fragment));
            else
            {
                if (!anchors.TryGetValue((node, fragment), out var anchor)) throw Invalid();
                if (isDynamic && anchor.Dynamic)
                {
                    Reserve(96L + 2L * fragment.Length);
                    dynamicReferences.Add(source, (fragment, anchor.Node));
                }
                else AddEdge(source, anchor.Node);
            }
        }
    }

    private int ResolvePointer(int node, string pointer)
    {
        var tokens = pointer.Split('/');
        for (int i = 1; i < tokens.Length; i++)
        {
            string keyword = tokens[i];
            if (positions.TryGetValue((node, keyword, null), out int child)) node = child;
            else if (++i < tokens.Length && positions.TryGetValue((node, keyword, tokens[i]), out child)) node = child;
            else throw Invalid();
        }
        return node;
    }

    private void CheckDepth(int documentCount)
    {
        var depths = new Dictionary<(int Node, DynamicScope? Scope), int>();
        var visiting = new HashSet<(int Node, DynamicScope? Scope)>();
        long remainingVisits = 8L * SafeFetchClient.SchemaLimit * documentCount;
        for (int i = 0; i < nodes.Count; i++) Depth(i, null, 1);

        int Depth(int id, DynamicScope? scope, int level)
        {
            if (--remainingVisits < 0 || level > 8) throw Invalid();
            int resource = nodes[id].Resource;
            if (dynamicResources.Contains(resource))
            {
                var current = scope;
                while (current != null && current.Resource != resource) current = current.Outer;
                if (current == null) scope = new(resource, scope);
            }
            var state = (id, scope);
            if (!depths.TryGetValue(state, out int depth))
            {
                Reserve(128);
                if (!visiting.Add(state)) throw Invalid();
                depth = 1;
                if (dynamicReferences.TryGetValue(id, out var reference))
                {
                    int target = reference.Fallback;
                    for (var frame = scope; frame != null; frame = frame.Outer)
                        if (anchors.TryGetValue((frame.Resource, reference.Name), out var match) && match.Dynamic)
                            target = match.Node;
                    depth += Depth(target, scope, level + 1);
                }
                if (nodes[id].Children is { } children)
                    foreach (int child in children)
                        depth = Math.Max(depth, 1 + Depth(child, scope, level + 1));
                depths.Add(state, depth);
                visiting.Remove(state);
            }
            if (level + depth - 1 > 8) throw Invalid();
            return depth;
        }
    }

    private static ManifestValidationException Invalid() => new(ManifestFailure.InvalidSchema);
}
