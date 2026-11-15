using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Integration.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Platform.Api.Identity;
using Xunit;

namespace Integration;

public sealed class OidcAuthenticationTests
{
    private sealed class AuthProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Path != "/test-auth") { await continuation(); return; }
                context.TraceIdentifier = Guid.NewGuid().ToString("N");
                var result = await context.AuthenticateAsync();
                if (!result.Succeeded) { await context.ChallengeAsync(); return; }
                await context.Response.WriteAsJsonAsync(new { subject = result.Principal!.FindFirst("sub")?.Value });
            });
            next(app);
        };
    }

    private static ApiFactory Factory(FakeOidcIssuer issuer) => new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:Platform"] = "Host=localhost;Database=unused;Username=unused",
        ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri,
        ["Oidc:Audience"] = "crm-api",
        ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
        ["Oidc:AllowHttpLoopback"] = "true"
    });

    private static string Forged(string alg, string issuer, byte[] key)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { alg, typ = "JWT" })));
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = issuer, sub = "fixture-user", aud = "crm-api", exp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        })));
        var input = header + "." + payload;
        var signature = alg == "none" ? "" : Base64UrlEncoder.Encode(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes(input)));
        return input + "." + signature;
    }

    [Fact]
    public async Task Valid_signed_token_authenticates_and_invalid_tokens_are_rejected()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        await using var other = await FakeOidcIssuer.StartAsync();
        await using var baseFactory = Factory(issuer);
        await using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>());
        });
        using var client = factory.CreateClient();
        var bearer = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("Bearer");
        var manager = Assert.IsType<ConfigurationManager<OpenIdConnectConfiguration>>(bearer.ConfigurationManager);
        Assert.Equal(TimeSpan.FromHours(12), manager.AutomaticRefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), manager.RefreshInterval);
        async Task<HttpResponseMessage> Send(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request);
        }
        using var valid = await Send(issuer.IssueToken(new()));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using var validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.Equal("fixture-user", validBody.RootElement.GetProperty("subject").GetString());
        var invalid = new[]
        {
            issuer.IssueToken(new() { IssuerOverride = other.Issuer.AbsoluteUri }),
            issuer.IssueToken(new() { Audience = "other" }),
            issuer.IssueToken(new() { OmitIssuer = true }),
            other.IssueToken(new() { IssuerOverride = issuer.Issuer.AbsoluteUri }),
            issuer.IssueToken(new() { Subject = "" }),
            issuer.IssueToken(new() { OmitSubject = true }),
            issuer.IssueToken(new() { OmitExpiration = true }),
            Forged("none", issuer.Issuer.AbsoluteUri, issuer.PublicKeyBytes()),
            Forged("HS256", issuer.Issuer.AbsoluteUri, issuer.PublicKeyBytes()),
            issuer.IssueToken(new() { Expires = DateTimeOffset.UtcNow.AddMinutes(-1), NotBefore = DateTimeOffset.UtcNow.AddMinutes(-2) }),
            issuer.IssueToken(new() { NotBefore = DateTimeOffset.UtcNow.AddMinutes(2) })
        };
        foreach (var token in invalid)
        {
            using var response = await Send(token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("AUTHENTICATION_REQUIRED", body.RootElement.GetProperty("reasonToken").GetString());
            Assert.Equal(response.Headers.GetValues("X-Correlation-Id").Single(), body.RootElement.GetProperty("correlationId").GetString());
        }
    }
    [Fact]
    public async Task Discovery_issuer_cannot_override_configured_issuer()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        await using var baseFactory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = "Host=localhost;Database=unused;Username=unused",
            ["Oidc:Issuer"] = "http://127.0.0.1:1234/",
            ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer, ".well-known/openid-configuration").AbsoluteUri,
            ["Oidc:AllowHttpLoopback"] = "true"
        });
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>()));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new()));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rotated_key_is_accepted_after_refresh_and_unreachable_discovery_fails_closed()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        await using var baseFactory = Factory(issuer);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>()));
        using var client = factory.CreateClient();
        async Task<HttpStatusCode> Send(string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        Assert.Equal(HttpStatusCode.OK, await Send(issuer.IssueToken(new())));
        issuer.RotateSigningKey();
        var rotated = issuer.IssueToken(new());
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpStatusCode status;
        do
        {
            status = await Send(rotated);
            if (status == HttpStatusCode.OK) break;
            await Task.Delay(100);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal(HttpStatusCode.OK, status);

        await using var deadFactory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = "Host=localhost;Database=unused;Username=unused",
            ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri,
            ["Oidc:Audience"] = "crm-api",
            ["Oidc:MetadataAddress"] = "http://127.0.0.1:1/.well-known/openid-configuration",
            ["Oidc:AllowHttpLoopback"] = "true"
        });
        await using var deadProbe = deadFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>()));
        using var deadClient = deadProbe.CreateClient();
        using var deadRequest = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
        deadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotated);
        using var deadResponse = await deadClient.SendAsync(deadRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, deadResponse.StatusCode);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Theory]
    [InlineData("Production", "http://127.0.0.1:1234/", "http://127.0.0.1:1234/metadata", true)]
    [InlineData("Testing", "http://example.com/", "http://example.com/metadata", true)]
    [InlineData("Testing", "http://127.0.0.1:1234/", "http://127.0.0.1:1234/metadata", false)]
    public void Unsafe_http_configuration_is_rejected(string environment, string issuer, string metadata, bool allow)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Oidc:Issuer"] = issuer, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = metadata,
            ["Oidc:AllowHttpLoopback"] = allow.ToString()
        }).Build();
        Assert.Throws<ArgumentException>(() => OidcOptions.Parse(config, new TestEnvironment(environment)));
    }

    [Fact]
    public async Task Discovery_redirect_is_not_followed()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync(redirectDiscovery: true);
        await using var baseFactory = Factory(issuer);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>()));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new()));
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, issuer.RedirectTargetHits);
    }

    [Fact]
    public async Task Production_http_oidc_configuration_fails_before_listener_with_safe_diagnostic()
    {
        var result = await MigratorProcess.RunAsync("Host=localhost;Database=unused;Username=unused", null, api: true,
            environment: new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production", ["Oidc__Issuer"] = "http://127.0.0.1:1/",
                ["Oidc__Audience"] = "crm-api", ["Oidc__MetadataAddress"] = "http://127.0.0.1:1/metadata",
                ["Oidc__AllowHttpLoopback"] = "true"
            });
        Assert.Equal(1, result.ExitCode);
        using var body = JsonDocument.Parse(result.StandardError);
        Assert.Equal("OIDC_CONFIGURATION_INVALID", body.RootElement.GetProperty("reasonToken").GetString());
        Assert.True(Guid.TryParseExact(body.RootElement.GetProperty("correlationId").GetString(), "N", out _));
    }

    [Fact]
    public async Task Production_https_discovery_never_follows_redirect_to_http()
    {
        const string marker = "CRM_TEST_TLS_CHILD";
        var fixturePath = Environment.GetEnvironmentVariable(marker);
        if (fixturePath is not null)
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(fixturePath, null);
            await using var target = await FakeOidcIssuer.StartAsync();
            await using var issuer = await FakeOidcIssuer.StartAsync(redirectDiscovery: true, tlsCertificate: cert,
                redirectUri: new Uri(target.Issuer, "redirect-target"));
            await using var baseFactory = Factory(issuer);
            await using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, AuthProbe>());
            });
            using var direct = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
            using var discovery = await direct.GetAsync(new Uri(issuer.Issuer, ".well-known/openid-configuration"));
            Assert.Equal(HttpStatusCode.Redirect, discovery.StatusCode);
            using var client = factory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test-auth");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issuer.IssueToken(new()));
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.True(issuer.DiscoveryHits >= 2, "API did not reach trusted HTTPS discovery.");
            Assert.Equal(0, target.RedirectTargetHits);
            return;
        }

        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("TLS trust fixture requires Linux.");
        var directory = Path.Combine(Path.GetTempPath(), "crm-oidc-tls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            using var key = RSA.Create(2048);
            var certRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            certRequest.CertificateExtensions.Add(names.Build());
            certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            certRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
            using var cert = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var pfx = Path.Combine(directory, "issuer.pfx");
            var pem = Path.Combine(directory, "root.pem");
            await File.WriteAllBytesAsync(pfx, cert.Export(X509ContentType.Pfx));
            await File.WriteAllTextAsync(pem, cert.ExportCertificatePem());
            File.SetUnixFileMode(pfx, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(OidcAuthenticationTests).Assembly.Location);
            start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName~Production_https_discovery_never_follows_redirect_to_http");
            start.Environment[marker] = pfx;
            start.Environment["SSL_CERT_FILE"] = pem;
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw; }
            var output = await stdout;
            var error = await stderr;
            Assert.True(process.ExitCode == 0, "Trusted TLS child test failed: " + output + error);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

}
