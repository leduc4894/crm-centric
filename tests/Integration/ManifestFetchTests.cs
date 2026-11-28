using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Platform.Infrastructure.Integration;
using Xunit;

namespace Integration;

public sealed class ManifestFetchTests
{
    private static readonly Uri Origin = new("https://manifest.test/");
    private static readonly byte[] Payload = "\uFEFF{ \"title\": \"Việt\" }\r\n"u8.ToArray();

    [Theory]
    [InlineData("http://manifest.test/")]
    [InlineData("https://name@manifest.test/")]
    [InlineData("https://@manifest.test/")]
    [InlineData("https://manifest.test/a?x")]
    [InlineData("https://manifest.test/a#fragment")]
    [InlineData("https://manifest.test/a?")]
    [InlineData("https://manifest.test/a#")]
    [InlineData("https://other.test/")]
    [InlineData("https://manifest.test:441/")]
    public async Task Invalid_target_rejected_before_dns(string target)
    {
        var client = new SafeFetchClient((_, _) => throw new InvalidOperationException("Unexpected DNS"));
        await Assert.ThrowsAsync<ManifestFetchException>(() =>
            client.FetchAsync(new Uri(target), new FetchPolicy(Origin), 1024, default));
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fd00::1")]
    [InlineData("100.100.100.200")]
    public async Task Mixed_dns_denied_even_if_tenant_approved_origin(string ip)
    {
        var client = new SafeFetchClient((_, _) => Task.FromResult(new[]{IPAddress.Parse("8.8.8.8"), IPAddress.Parse(ip)}));
        var policy = new FetchPolicy(new Uri("https://module.test"), [Origin]);
        var error = await Assert.ThrowsAsync<ManifestFetchException>(() => client.FetchAsync(Origin, policy, 1024, default));
        Assert.Equal(FetchFailure.AddressDenied, error.Reason);
    }

    [Theory]
    [InlineData("127.0.0.1", "0.0.0.0/0")]
    [InlineData("169.254.169.254", "0.0.0.0/0")]
    [InlineData("fd00:ec2::254", "fc00::/7")]
    [InlineData("fd20:ce::254", "fc00::/7")]
    public void Operator_network_cannot_override_reserved_addresses(string address, string network)
    {
        var policy = new FetchPolicy(Origin, operatorPrivateNetworks: [System.Net.IPNetwork.Parse(network)]);
        Assert.False(policy.Allows(IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Https_pins_address_validates_hostname_and_bounds_entire_response()
    {
        var address = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=manifest.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("manifest.test");
        request.CertificateExtensions.Add(names.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(address, 0, listen => listen.UseHttps(cert)));
        await using var app = builder.Build();
        using var forbidden = new TcpListener(IPAddress.Loopback, 0);
        forbidden.Start();
        var forbiddenUri = $"https://127.0.0.1:{((IPEndPoint)forbidden.LocalEndpoint).Port}/denied";
        int requests = 0;
        app.Run(async ctx =>
        {
            Interlocked.Increment(ref requests);
            switch (ctx.Request.Path.Value)
            {
                case "/redirect": ctx.Response.Redirect(forbiddenUri); return;
                case "/loop": ctx.Response.Redirect("/loop"); return;
                case "/error": ctx.Response.StatusCode = 503; return;
                case "/slow":
                    await ctx.Response.StartAsync();
                    await Task.Delay(Timeout.Infinite, ctx.RequestAborted);
                    return;
            }
            await ctx.Response.StartAsync();
            await ctx.Response.Body.WriteAsync(Payload);
        });
        await app.StartAsync();
        var origin = new UriBuilder(app.Urls.Single()) { Host = "manifest.test" }.Uri;
        using var rebound = new TcpListener(IPAddress.Loopback, origin.Port);
        rebound.Start();
        var policy = new FetchPolicy(origin, operatorPrivateNetworks: [new System.Net.IPNetwork(address, 32)]);
        var answer = address;
        var client = new SafeFetchClient((_, _) => Task.FromResult(new[]{answer}), cert);
        var result = await client.FetchAsync(origin, policy, Payload.Length, default);
        Assert.Equal(Payload, result.Bytes.ToArray());
        Assert.Equal(origin, result.FinalUri);
        Assert.Equal(1, requests);

        answer = IPAddress.Loopback;
        await Check(origin, policy, FetchFailure.AddressDenied);
        answer = address;
        var wrongName = new UriBuilder(origin) { Host = "wrong.test" }.Uri;
        await Check(wrongName, new FetchPolicy(wrongName, operatorPrivateNetworks: [new System.Net.IPNetwork(address, 32)]), FetchFailure.Transport);
        Assert.Equal(1, requests);
        await Check(new Uri(origin, "redirect"), policy, FetchFailure.HttpStatus);
        await Check(new Uri(origin, "loop"), policy, FetchFailure.HttpStatus);
        await Check(new Uri(origin, "error"), policy, FetchFailure.HttpStatus);
        await Check(origin, policy, FetchFailure.TooLarge, Payload.Length - 1);
        await Check(new Uri(origin, "slow"), policy, FetchFailure.Timeout);
        Assert.Equal(6, requests);
        Assert.False(forbidden.Pending());
        Assert.False(rebound.Pending());

        async Task Check(Uri target, FetchPolicy p, FetchFailure expected, int limit = 1024)
        {
            var error = await Assert.ThrowsAsync<ManifestFetchException>(() => client.FetchAsync(target, p, limit, default));
            Assert.Equal(expected, error.Reason);
        }
    }
}
