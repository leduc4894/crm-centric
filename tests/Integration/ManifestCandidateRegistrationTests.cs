using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;
using Platform.Infrastructure.Integration;
using Xunit;

namespace Integration;

public sealed partial class ManifestRegistryTests
{
    [Theory]
    [InlineData("exact", null, null, 6)]
    [InlineData("manifest-size", "ManifestFetch", FetchFailure.TooLarge, 1)]
    [InlineData("sidecar-size", "SidecarFetch", FetchFailure.TooLarge, 2)]
    [InlineData("changed", "Publisher", PublisherFailure.InvalidSignature, 2)]
    [InlineData("namespace", "ManifestValidation", ManifestFailure.NamespaceDenied, 2)]
    [InlineData("schema-hash", "Schemas", ManifestFailure.SchemaHash, 3)]
    [InlineData("schema-size", "Schemas", FetchFailure.TooLarge, 3)]
    [InlineData("storage", "Registry", RegistryFailure.Storage, 6)]
    public async Task RegistersCandidateThroughGuardedHttps(string scenario, string? failedStage, Enum? expectedReason, int expectedRequests)
    {
        var address = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        using var tlsKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=demo.example.net", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("demo.example.net");
        request.CertificateExtensions.Add(names.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(address, 0, listen => listen.UseHttps(cert)));
        await using var server = builder.Build();
        var paths = new ConcurrentQueue<string>();
        var bodies = new Dictionary<string, byte[]>();
        server.Run(async http =>
        {
            string path = http.Request.Path + http.Request.QueryString;
            paths.Enqueue(path);
            if (bodies.TryGetValue(path, out var bytes))
                await http.Response.Body.WriteAsync(bytes);
            else
                http.Response.StatusCode = 404;
        });
        await server.StartAsync();
        var origin = new UriBuilder(server.Urls.Single()) { Host = "demo.example.net" }.Uri;
        string[] expectedPaths = [
            "/catalog/releases/demo.json", "/catalog/releases/demo.json.sig.json", "/schemas/demo-event.schema.json",
            "/schemas/demo-echo-input.schema.json", "/schemas/demo-echo-output.schema.json", "/schemas/demo-panel.schema.json"
        ];
        var json = Encoding.UTF8.GetString(Read("manifest.json")).Replace("https://demo.example.net", origin.GetLeftPart(UriPartial.Authority));
        var manifest = Encoding.UTF8.GetBytes(json.PadRight(scenario == "manifest-size" ? 262145 : 262144));
        using var signingKey = Key.Create(SignatureAlgorithm.Ed25519);
        var signature = SignatureAlgorithm.Ed25519.Sign(signingKey, manifest);
        string envelope = JsonSerializer.Serialize(new { algorithm = "Ed25519", keyId = KeyId, signature = Base64Url.EncodeToString(signature) });
        bodies[expectedPaths[0]] = manifest;
        if (scenario == "changed") manifest[^1] = (byte)'\n';
        bodies[expectedPaths[1]] = Encoding.UTF8.GetBytes(envelope.PadRight(scenario == "sidecar-size" ? 4097 : 4096));
        foreach (string path in expectedPaths.Skip(2))
            bodies[path] = File.ReadAllBytes(Path.Combine(ContainerImages.Root, "docs/contracts", Path.GetFileName(path)));
        if (scenario == "schema-hash") bodies[expectedPaths[2]] = "{}"u8.ToArray();
        if (scenario == "schema-size") bodies[expectedPaths[2]] = new byte[65537];
        if (scenario == "storage") await Scalar("ALTER TABLE platform.manifest_schemas ADD CHECK (ordinal < 1)");

        var entry = new PublisherTrustEntry(Module, KeyId, [.. signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey)],
            [origin], PublisherKeyStatus.Active);
        var trust = new PublisherTrustSnapshot(7, [entry], scenario == "namespace" ? [] : [new(Module, "demo.")]);
        var correlationId = Guid.NewGuid();
        var policy = new FetchPolicy(origin, operatorPrivateNetworks: [new System.Net.IPNetwork(address, 32)]);
        var client = new SafeFetchClient((_, _) => Task.FromResult(new[] { address }), cert);
        var operation = new ManifestCandidateRegistration(client, registry);
        Task<RegistryResult> Register() => operation.RegisterAsync(new Uri(origin, expectedPaths[0]), policy, trust, correlationId, default);
        if (failedStage is null)
        {
            Assert.Equal(RegistryResult.Inserted, await Register());
            Assert.Equal(manifest, (byte[])(await Scalar("SELECT manifest_bytes FROM platform.manifest_candidates"))!);
            Assert.Equal(4L, await Scalar("SELECT count(*) FROM platform.manifest_schemas"));
        }
        else
        {
            var error = await Assert.ThrowsAsync<CandidateRegistrationException>(Register);
            Assert.Equal(correlationId, error.CorrelationId);
            Assert.Equal(failedStage, error.Stage.ToString());
            Assert.Equal(expectedReason, error.Reason);
            Assert.Equal(0L, await Scalar("SELECT count(*) FROM platform.manifest_candidates"));
            Assert.Equal(0L, await Scalar("SELECT count(*) FROM platform.manifest_schemas"));
        }
        Assert.Equal(expectedPaths.Take(expectedRequests).ToArray(), paths.ToArray());
    }
}
