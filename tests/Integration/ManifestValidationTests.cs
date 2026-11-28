using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using NSec.Cryptography;
using Platform.Infrastructure.Integration;
using Xunit;

namespace Integration;

public class ManifestValidationTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "../../../../../");
    private static readonly string Fixture = Path.Combine(Root, "tests/Contract/fixtures/wave3-publisher");
    private static readonly Uri Origin = new("https://demo.example.net");
    private const string Module = "com.example.demo";
    private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(Fixture, file));
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static JsonNode Demo() => JsonNode.Parse(Read("manifest.json"))!;
    private static VerifiedPublisher Proof(byte[] bytes, string? prefix = "demo.") =>
        new(Module, "fixture-publisher-1", 1, Hash(bytes), prefix, [Origin]);
    private static ValidatedManifest Validate(JsonNode node, string? prefix = "demo.")
    {
        byte[] bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        return ManifestValidator.Validate(bytes, Proof(bytes, prefix));
    }

    [Fact]
    public async Task SignedDemoUsesExactPinnedSchemas()
    {
        var entry = new PublisherTrustEntry(Module, "fixture-publisher-1",
            [.. PublicKey.Import(SignatureAlgorithm.Ed25519, Read("public.pem"), KeyBlobFormat.PkixPublicKeyText)
                .Export(KeyBlobFormat.RawPublicKey)], [Origin], PublisherKeyStatus.Active);
        var trust = new PublisherTrustSnapshot(1, [entry], [new(Module, "demo.")]);
        var bytes = Read("manifest.json");
        var body = new FetchResult([.. bytes], new Uri(Origin, "manifest.json"));
        var sidecar = new FetchResult([.. Read("manifest.json.sig.json")], new Uri(Origin, "manifest.json.sig.json"));
        var proof = PublisherVerifier.Verify(body, sidecar, trust);
        var manifest = ManifestValidator.Validate(bytes, proof);
        Assert.Equal(bytes, manifest.Bytes.ToArray());
        Assert.Equal(trust.Revision, manifest.Publisher.TrustRevision);
        var loader = new SchemaSnapshotLoader((uri, _, _) => Task.FromResult(new FetchResult(
            [.. File.ReadAllBytes(Path.Combine(Root, "docs/contracts", Path.GetFileName(uri.AbsolutePath)))], uri)));
        var snapshots = await loader.LoadAsync(manifest, new(Origin), default);
        Assert.Equal(4, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal(s.Reference.Sha256, Hash(s.Bytes.ToArray())));
        var revoked = new PublisherTrustSnapshot(2, [entry]);
        var current = PublisherVerifier.Verify(body, sidecar, revoked);
        Assert.Throws<ManifestValidationException>(() => ManifestValidator.Validate(bytes, current));
    }

    [Theory]
    [InlineData("events")] [InlineData("panels")] [InlineData("actions")]
    public void RejectsRepeatedDescriptors(string kind)
    {
        var demo = Demo();
        demo[kind]!.AsArray().Add(demo[kind]![0]!.DeepClone());
        Assert.Throws<ManifestValidationException>(() => Validate(demo));
    }

    [Theory]
    [InlineData("demox.created.v1")] [InlineData("demo.v1")] [InlineData("demo..created.v1")]
    [InlineData("demo.created.v0")] [InlineData("Demo.created.v1")]
    public void RejectsInvalidEvent(string type)
    {
        var demo = Demo();
        demo["events"]![0]!["type"] = type;
        Assert.Throws<ManifestValidationException>(() => Validate(demo));
    }

    [Theory]
    [InlineData("demo.", "demo.")] [InlineData("demo.", "demo.activity.")]
    [InlineData("demo.activity.", "demo.")] [InlineData("crm.activity.", "ok.")]
    public void RejectsConflictingConfiguration(string first, string second)
    {
        Assert.Throws<ManifestValidationException>(() => new PublisherTrustSnapshot(2, [],
            [new(Module, first), new("another.module", second)]));
    }

    [Fact]
    public void NamespaceRequiredOnlyForEventPublishers()
    {
        var demo = Demo();
        Assert.Throws<ManifestValidationException>(() => Validate(demo, null));
        demo.AsObject().Remove("events");
        var capabilities = demo["capabilities"]!.AsArray();
        capabilities.Remove(capabilities.First(c => c!.GetValue<string>() == "event.trigger"));
        Assert.Empty(Validate(demo, null).Document.EnumerateObject().Where(p => p.Name == "events"));
    }

    [Theory]
    [InlineData("demo")] [InlineData("demo..")] [InlineData(" demo.")] [InlineData("crm.")]
    public void RejectsMalformedOrReservedPrefix(string prefix)
    {
        Assert.Throws<ManifestValidationException>(() => new PublisherTrustSnapshot(1, [], [new(Module, prefix)]));
    }

    [Fact]
    public void OneModuleCannotHaveTwoAssignments()
    {
        Assert.Throws<ManifestValidationException>(() => new PublisherTrustSnapshot(1, [],
            [new(Module, "alpha."), new(Module, "beta.")]));
    }

    [Theory]
    [InlineData("scope")] [InlineData("entity")] [InlineData("version")] [InlineData("base")]
    public void RejectsInvalidManifest(string change)
    {
        var demo = Demo();
        switch (change)
        {
            case "scope": demo["events"]![0]!["requiredScopes"]![0] = "missing"; break;
            case "entity": demo["actions"]![0]!.AsObject().Remove("entityType"); break;
            case "version": demo["version"] = "01.0.0"; break;
            case "base": demo["baseUrl"] = "https://other.test"; break;
        }
        Assert.Throws<ManifestValidationException>(() => Validate(demo));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CatalogOriginRequiresExplicitApproval(bool approved)
    {
        var catalog = new Uri("https://catalog.example/");
        var demo = Demo();
        demo["capabilities"] = new JsonArray("event.trigger");
        foreach (var field in new[] { "navigationUrl", "healthUrl", "panels", "actions" }) demo.AsObject().Remove(field);
        demo["events"]![0]!["schema"]!["uri"] = new Uri(catalog, "demo-event.schema.json").AbsoluteUri;
        var calls = new List<Uri>();
        var loader = new SchemaSnapshotLoader((uri, policy, _) => {
            calls.Add(uri);
            policy.Validate(uri);
            return Task.FromResult(new FetchResult([.. File.ReadAllBytes(Path.Combine(Root, "docs/contracts",
                Path.GetFileName(uri.AbsolutePath)))], uri));
        });
        var policy = new FetchPolicy(catalog, approved ? [catalog] : []);
        var manifest = Validate(demo);
        if (approved) Assert.Single(await loader.LoadAsync(manifest, policy, default));
        else
        {
            var error = await Assert.ThrowsAsync<ManifestFetchException>(() => loader.LoadAsync(manifest, policy, default));
            Assert.Equal(FetchFailure.OriginDenied, error.Reason);
            Assert.Empty(calls);
        }
    }

    [Theory]
    [InlineData(32_768, true)]
    [InlineData(2_048, false)]
    public async Task BudgetsThePinnedSchemaIndexDuringConstruction(int bytesPerDocument, bool fits)
    {
        var demo = Demo();
        var properties = new JsonObject();
        for (var i = 0; i < 60; i++)
            properties[$"field{i}"] = new JsonObject { ["type"] = "string" };
        var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["properties"] = properties }.ToJsonString());
        var hash = Hash(bytes);
        JsonNode[] declared = [demo["events"]![0]!["schema"]!, demo["actions"]![0]!["inputSchema"]!,
            demo["actions"]![0]!["outputSchema"]!, demo["panels"]![0]!["outputSchema"]!];
        foreach (var schema in declared)
        {
            schema["sha256"] = hash;
            var filename = Path.GetFileName(new Uri(schema["uri"]!.GetValue<string>()).AbsolutePath);
            schema["uri"] = new Uri(Origin, new string('a', 300) + "/" + filename).AbsoluteUri;
        }
        // A later malformed document distinguishes early construction rejection from a post-index check.
        var lastBytes = fits ? bytes : """{"type":42}"""u8.ToArray();
        declared[^1]["sha256"] = Hash(lastBytes);
        var lastUri = new Uri(declared[^1]["uri"]!.GetValue<string>());
        var loader = new SchemaSnapshotLoader((uri, _, _) =>
            Task.FromResult(new FetchResult([.. (uri == lastUri ? lastBytes : bytes)], uri)), bytesPerDocument);
        var manifest = Validate(demo);
        if (fits) Assert.Equal(4, (await loader.LoadAsync(manifest, new(Origin), default)).Count);
        else
        {
            var error = await Assert.ThrowsAsync<ManifestValidationException>(() => loader.LoadAsync(manifest, new(Origin), default));
            Assert.Equal(ManifestFailure.SchemaComplexity, error.Reason);
        }
    }

    [Theory]
    [InlineData("peer")]
    [InlineData("embedded-peer")]
    [InlineData("unlisted")]
    [InlineData("cycle")]
    [InlineData("too-deep")]
    [InlineData("ambiguous-id")]
    public async Task ResolvesOnlyTheCompletePinnedSchemaGraph(string scenario)
    {
        var demo = Demo();
        var eventRef = demo["events"]![0]!["schema"]!;
        var inputRef = demo["actions"]![0]!["inputSchema"]!;
        string target = scenario switch
        {
            "embedded-peer" => "shared#name",
            "unlisted" => "unlisted.json",
            _ => "demo-echo-input.schema.json"
        };
        var eventSchema = Encoding.UTF8.GetBytes(new JsonObject { ["$ref"] = target }.ToJsonString());
        string input = scenario switch
        {
            "embedded-peer" => """{"$defs":{"shared":{"$id":"shared","$anchor":"name","type":"string"}}}""",
            "cycle" => """{"$ref":"demo-event.schema.json"}""",
            "too-deep" => string.Concat(Enumerable.Repeat("""{"not":""", 7)) + "true" + new string('}', 7),
            "ambiguous-id" => """{"$id":"demo-event.schema.json"}""",
            _ => """{"type":"string"}"""
        };
        var inputSchema = Encoding.UTF8.GetBytes(input);
        eventRef["sha256"] = Hash(eventSchema);
        inputRef["sha256"] = Hash(inputSchema);
        var manifest = Validate(demo);
        var calls = new List<Uri>();
        var loader = new SchemaSnapshotLoader((uri, _, _) =>
        {
            calls.Add(uri);
            string filename = Path.GetFileName(uri.AbsolutePath);
            byte[] bytes = filename switch
            {
                "demo-event.schema.json" => eventSchema,
                "demo-echo-input.schema.json" => inputSchema,
                _ => File.ReadAllBytes(Path.Combine(Root, "docs/contracts", filename))
            };
            return Task.FromResult(new FetchResult([.. bytes], uri));
        });

        if (scenario is "peer" or "embedded-peer")
            Assert.Equal(4, (await loader.LoadAsync(manifest, new(Origin), default)).Count);
        else
            Assert.Equal(ManifestFailure.InvalidSchema, (await Assert.ThrowsAsync<ManifestValidationException>(
                () => loader.LoadAsync(manifest, new(Origin), default))).Reason);
        Assert.Equal(manifest.SchemaReferences.Select(r => r.Uri).ToArray(), calls.ToArray());
    }

    [Theory]
    [InlineData("ambiguous-fragment")] [InlineData("escaped-pointer")]
    [InlineData("unrelated")] [InlineData("overrideCycle")]
    [InlineData("inert")] [InlineData("dynamicRemote")] [InlineData("dynamicCycle")]
    [InlineData("resources")] [InlineData("dynamic")] [InlineData("resourceCycle")]
    [InlineData("local")] [InlineData("refdepth")] [InlineData("badpattern")]
    [InlineData("external")] [InlineData("cycle")] [InlineData("depth")] [InlineData("invalid")]
    [InlineData("oversize")] [InlineData("limit")] [InlineData("hash")] [InlineData("missing")] [InlineData("cross")]
    public async Task ChecksSchemasBeforeReturningSnapshots(string change)
    {
        var demo = Demo();
        var schema = demo["events"]![0]!["schema"]!;
        var payload = change switch {
            "ambiguous-fragment" => """{"$defs":{"child":{"$anchor":"value","type":"string"}},"$ref":"##value"}""",
            "escaped-pointer" => """{"$defs":{"a/b~c":{"items":{"type":"string"}}},"$ref":"#/$defs/a~1b~0c/items"}""",
            "unrelated" => """
                {"$ref":"a","$defs":{
                    "a":{"$id":"a","$dynamicAnchor":"x","$dynamicRef":"#y","$defs":{"leaf":{"$dynamicAnchor":"y"}}},
                    "b":{"$id":"b","$dynamicAnchor":"y","$dynamicRef":"#x","$defs":{"leaf":{"$dynamicAnchor":"x"}}}
                }}
                """,
            "overrideCycle" => """
                {"$dynamicAnchor":"leaf","$ref":"nested","$defs":{
                    "nested":{"$id":"nested","$dynamicRef":"#leaf","$defs":{"value":{"$dynamicAnchor":"leaf"}}}
                }}
                """,
            "inert" => """{"properties":{"value":{"$id":"nested","$dynamicAnchor":"value","type":"integer"}}}""",
            "dynamicRemote" => """{"$dynamicRef":"https://forbidden.test/schema#name"}""",
            "dynamicCycle" => """{"$dynamicAnchor":"here","$dynamicRef":"#here"}""",
            "resources" => """{"$defs":{"child":{"$id":"child.json","type":"string","$dynamicAnchor":"name"}},"$ref":"child.json#name"}""",
            "dynamic" => """{"$defs":{"child":{"$dynamicAnchor":"name","type":"string"}},"$dynamicRef":"#name"}""",
            "resourceCycle" => """{"$defs":{"child":{"$id":"child.json","$ref":"#"}},"$ref":"child.json"}""",
            "local" => """{"$defs":{"name":{"type":"string"}},"properties":{"name":{"$ref":"#/$defs/name"}}}""",
            "badpattern" => """{"pattern":"("}""",
            "refdepth" => "{\"$ref\":\"#/$defs/s1\",\"$defs\":{" +
                string.Join(",", Enumerable.Range(1, 8).Select(i => $"\"s{i}\":{{\"$ref\":\"#/$defs/s{i+1}\"}}")) + ",\"s9\":{}}}",
            "external" => """{"$ref":"https://forbidden.test/schema"}""",
            "cycle" => """{"$defs":{"a":{"$ref":"#/$defs/a"}},"$ref":"#/$defs/a"}""",
            "depth" => string.Concat(Enumerable.Repeat("""{"items":""", 9)) + "{}" + new string('}', 9),
            "invalid" => """{"type":42}""",
            "oversize" => "{}".PadRight(65537),
            "limit" => "{}".PadRight(65536),
            _ => "{}"
        };
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        if (change != "hash") schema["sha256"] = Hash(bytes);
        if (change == "cross") schema["uri"] = "https://other.test/schema";
        var manifest = Validate(demo);
        var calls = new List<Uri>();
        var loader = new SchemaSnapshotLoader((uri, _, _) => {
            calls.Add(uri);
            if (change == "missing") throw new ManifestFetchException(FetchFailure.HttpStatus);
            byte[] result = uri.AbsolutePath.EndsWith("demo-event.schema.json") ? bytes :
                File.ReadAllBytes(Path.Combine(Root, "docs/contracts", Path.GetFileName(uri.AbsolutePath)));
            return Task.FromResult(new FetchResult([..result], uri));
        });
        async Task Run() => await loader.LoadAsync(manifest, new(Origin), default);
        if (change is "limit" or "local" or "resources" or "dynamic" or "inert" or "unrelated" or "escaped-pointer") await Run();
        else if (change is "cross" or "missing") await Assert.ThrowsAsync<ManifestFetchException>(Run);
        else await Assert.ThrowsAsync<ManifestValidationException>(Run);
        Assert.All(calls, uri => Assert.Equal(Origin.Host, uri.Host));
        if (change == "cross")
        {
            Assert.Equal("other.test", Assert.Single(manifest.RequestedOrigins).Host);
            Assert.Empty(calls);
        }
    }
}
