using System.Text;
using System.Text.Json.Nodes;
using NSec.Cryptography;
using Platform.Infrastructure.Integration;
using Xunit;

namespace Integration;

public class PublisherVerifierTests
{
    private const string Module = "com.example.demo";
    private const string KeyId = "fixture-publisher-1";
    private static readonly Uri Origin = new("https://publisher.test/");
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "../../../../Contract/fixtures/wave3-publisher");
    private static byte[] Read(string file) => File.ReadAllBytes(Path.Combine(DirectoryPath, file));
    private static FetchResult Fetch(byte[] bytes, string file) => new([.. bytes], new Uri(Origin, file));
    private readonly FetchResult manifest = Fetch(Read("manifest.json"), "manifest.json");
    private readonly FetchResult sidecar = Fetch(Read("manifest.json.sig.json"), "manifest.json.sig.json");
    private readonly PublisherTrustEntry entry = new(Module, KeyId,
        [.. PublicKey.Import(SignatureAlgorithm.Ed25519, Read("public.pem"), KeyBlobFormat.PkixPublicKeyText)
            .Export(KeyBlobFormat.RawPublicKey)], [Origin], PublisherKeyStatus.Active);

    private VerifiedPublisher Verify(FetchResult? body = null, FetchResult? sig = null, PublisherTrustEntry? trust = null)
        => PublisherVerifier.Verify(body ?? manifest, sig ?? sidecar, new(1, [trust ?? entry]));

    [Fact]
    public void KnownAnswer()
    {
        Assert.Equal(new VerifiedPublisher(Module, KeyId, 1, "82478c0cc6aa2e468f3fe87f73a13f4b2e482bdeb764e5947bf8b9e5083d1c52", null, entry.AllowedOrigins), Verify());
    }

    [Theory]
    [InlineData("whitespace")]
    [InlineData("one-byte")]
    [InlineData("duplicate")]
    public void RejectsChangedBody(string change)
    {
        string original = Encoding.UTF8.GetString(manifest.Bytes.AsSpan());
        string altered = change switch {
            "one-byte" => original.Replace("Demo Module", "demo Module"),
            "duplicate" => original.Replace("\"type\":", "\"type\":null,\"type\":"),
            _ => original + " "
        };
        var failure = Assert.Throws<PublisherVerificationException>(() => Verify(Fetch(Encoding.UTF8.GetBytes(altered), "manifest.json")));
        Assert.Equal(change == "duplicate" ? PublisherFailure.InvalidManifest : PublisherFailure.InvalidSignature, failure.Reason);
    }

    [Theory]
    [InlineData("module")] [InlineData("keyId")] [InlineData("wrong-key")] [InlineData("short-key")]
    [InlineData("revoked")] [InlineData("origin")] [InlineData("sidecar-origin")]
    public void RejectsUntrustedInputs(string change)
    {
        var trust = change switch {
            "module" => entry with { ModuleId = "other" },
            "keyId" => entry with { KeyId = "unknown" },
            "wrong-key" => entry with { PublicKey = [.. new byte[32]] },
            "short-key" => entry with { PublicKey = [1] },
            "revoked" => entry with { Status = PublisherKeyStatus.Revoked },
            "origin" => entry with { AllowedOrigins = [new Uri("https://demo.example.net")] },
            _ => entry
        };
        var sig = change == "sidecar-origin" ? sidecar with { FinalUri = new("https://other.test/manifest.json.sig.json") } : sidecar;
        Assert.Throws<PublisherVerificationException>(() => Verify(sig: sig, trust: trust));
    }

    [Theory]
    [InlineData("duplicate")] [InlineData("malformed")] [InlineData("missing")] [InlineData("extra")]
    [InlineData("algorithm")] [InlineData("padding")] [InlineData("unused-bits")] [InlineData("short")]
    public void RejectsInvalidSidecar(string change)
    {
        var json = JsonNode.Parse(sidecar.Bytes.AsSpan())!.AsObject();
        string signature = json["signature"]!.GetValue<string>();
        switch (change) {
            case "missing": json.Remove("signature"); break;
            case "extra": json["extra"] = true; break;
            case "algorithm": json["algorithm"] = "Ed448"; break;
            case "padding": json["signature"] = signature + "=="; break;
            case "unused-bits":
                const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
                json["signature"] = signature[..^1] + alphabet[alphabet.IndexOf(signature[^1]) + 1]; break;
            case "short": json["signature"] = "AA"; break;
        }
        string text = json.ToJsonString();
        if (change == "duplicate") text = text.Insert(1, "\"algorithm\":\"Ed25519\",");
        if (change == "malformed") text = "{";
        Assert.Throws<PublisherVerificationException>(() => Verify(sig: Fetch(Encoding.UTF8.GetBytes(text), "manifest.json.sig.json")));
    }

    [Theory]
    [InlineData("invalid.key")]
    [InlineData("invalid key")]
    public void InvalidKeyIdFailsEvenWhenConfigured(string keyId)
    {
        var json = JsonNode.Parse(sidecar.Bytes.AsSpan())!.AsObject();
        json["keyId"] = keyId;
        var sig = Fetch(Encoding.UTF8.GetBytes(json.ToJsonString()), "manifest.json.sig.json");
        var failure = Assert.Throws<PublisherVerificationException>(() => Verify(sig: sig, trust: entry with { KeyId = keyId }));
        Assert.Equal(PublisherFailure.InvalidSidecar, failure.Reason);
    }

    [Fact]
    public void UnpairedSurrogateFailsWithTypedReason()
    {
        var text = Encoding.UTF8.GetString(sidecar.Bytes.AsSpan()).Replace(KeyId, @"\uD800");
        Assert.Throws<PublisherVerificationException>(() => Verify(sig: Fetch(Encoding.UTF8.GetBytes(text), "manifest.json.sig.json")));
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("publicKey")]
    [InlineData("allowedOrigins")]
    public void MissingOperatorTrustMaterialHasTypedFailure(string missing)
    {
        var trust = missing switch {
            "entries" => new PublisherTrustSnapshot(1, default),
            "publicKey" => new PublisherTrustSnapshot(1, [entry with { PublicKey = default }]),
            _ => new PublisherTrustSnapshot(1, [entry with { AllowedOrigins = default }])
        };
        var failure = Assert.Throws<PublisherVerificationException>(() => PublisherVerifier.Verify(manifest, sidecar, trust));
        Assert.Equal(missing == "allowedOrigins" ? PublisherFailure.OriginDenied : PublisherFailure.UntrustedKey, failure.Reason);
    }

    [Fact]
    public void RotationUsesCurrentSnapshot()
    {
        using var rotated = new Key(SignatureAlgorithm.Ed25519);
        var replacement = entry with { KeyId = "next", PublicKey = [.. rotated.PublicKey.Export(KeyBlobFormat.RawPublicKey)] };
        var trust = new PublisherTrustSnapshot(2, [entry with { Status = PublisherKeyStatus.Revoked }, replacement], [new(Module, "demo.")]);
        Assert.Throws<PublisherVerificationException>(() => PublisherVerifier.Verify(manifest, sidecar, trust));
        var json = JsonNode.Parse(sidecar.Bytes.AsSpan())!.AsObject();
        json["keyId"] = replacement.KeyId;
        json["signature"] = System.Buffers.Text.Base64Url.EncodeToString(SignatureAlgorithm.Ed25519.Sign(rotated, manifest.Bytes.AsSpan()));
        var sig = Fetch(Encoding.UTF8.GetBytes(json.ToJsonString()), "manifest.json.sig.json");
        var verified = PublisherVerifier.Verify(manifest, sig, trust);
        Assert.Equal(2, verified.TrustRevision);
        Assert.Equal("demo.", verified.EventNamespace);
    }
}
