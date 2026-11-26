using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Integration.Fixtures;

public sealed record FakeTokenOptions
{
    public string? IssuerOverride { get; init; }
    public bool OmitIssuer { get; init; }
    public string Audience { get; init; } = "crm-api";
    public string Subject { get; init; } = "fixture-user";
    public string? Scope { get; init; }
    public string? PayloadType { get; init; }
    public bool IncludeVerifiedEmail { get; init; }
    public string? Role { get; init; }
    public bool OmitSubject { get; init; }
    public bool OmitExpiration { get; init; }
    public DateTimeOffset? IssuedAt { get; init; }
    public DateTimeOffset? NotBefore { get; init; }
    public DateTimeOffset? Expires { get; init; }
}

public sealed class FakeOidcIssuer : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly object keyLock = new();
    private readonly List<SigningKey> signingKeys = [];
    private readonly JsonWebTokenHandler tokenHandler = new() { SetDefaultTimesOnTokenCreation = false };
    private bool disposed;
    private int redirectTargetHits;
    private int discoveryHits;
    public int DiscoveryHits => Volatile.Read(ref discoveryHits);
    public int RedirectTargetHits => Volatile.Read(ref redirectTargetHits);

    private FakeOidcIssuer(WebApplication application)
    {
        this.application = application;
        signingKeys.Add(SigningKey.Create());
    }

    public Uri Issuer { get; private set; } = null!;
    public Uri JwksUri => new(Issuer, ".well-known/jwks.json");

    public static async Task<FakeOidcIssuer> StartAsync(CancellationToken cancellationToken = default, bool redirectDiscovery = false, X509Certificate2? tlsCertificate = null, Uri? redirectUri = null, bool bindAny = false, string? publicHost = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(bindAny ? IPAddress.Any : IPAddress.Loopback, 0, options =>
        {
            if (tlsCertificate is not null) options.UseHttps(tlsCertificate);
        }));
        var application = builder.Build();
        FakeOidcIssuer? fixture = null;
        try
        {
            fixture = new FakeOidcIssuer(application);
            application.MapGet("/.well-known/openid-configuration", () =>
            {
                Interlocked.Increment(ref fixture.discoveryHits);
                return redirectDiscovery
                    ? Results.Redirect(redirectUri?.AbsoluteUri ?? "/redirect-target")
                    : Results.Json(new { issuer = fixture.Issuer.AbsoluteUri, jwks_uri = fixture.JwksUri.AbsoluteUri });
            });
            application.MapGet("/redirect-target", () =>
            {
                Interlocked.Increment(ref fixture.redirectTargetHits);
                return Results.Json(new { issuer = fixture.Issuer.AbsoluteUri, jwks_uri = fixture.JwksUri.AbsoluteUri });
            });
            application.MapGet("/.well-known/jwks.json", () => new { keys = fixture.PublicKeys() });
            await application.StartAsync(cancellationToken);
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!;
            var address = new Uri(addresses.Addresses.Single());
            fixture.Issuer = publicHost is null ? address : new UriBuilder(address) { Host = publicHost }.Uri;
            return fixture;
        }
        catch
        {
            if (fixture is not null)
                await fixture.DisposeAsync();
            else
                await application.DisposeAsync();
            throw;
        }
    }

    public string IssueToken(FakeTokenOptions options)
    {
        lock (keyLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = DateTimeOffset.UtcNow;
            var claims = new Dictionary<string, object>();
            if (!options.OmitSubject) claims["sub"] = options.Subject;
            if (options.PayloadType is not null) claims["typ"] = options.PayloadType;
            if (options.IncludeVerifiedEmail)
            {
                claims["email"] = "Alice@Example.COM";
                claims["email_verified"] = true;
            }
            if (options.Scope is not null) claims["scope"] = options.Scope;
            if (options.Role is not null) claims["role"] = options.Role;
            return tokenHandler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = options.OmitIssuer ? null : options.IssuerOverride ?? Issuer.AbsoluteUri,
                Audience = options.Audience,
                Claims = claims,
                IssuedAt = (options.IssuedAt ?? now.AddMinutes(-1)).UtcDateTime,
                NotBefore = (options.NotBefore ?? now.AddMinutes(-1)).UtcDateTime,
                Expires = options.OmitExpiration ? null : (options.Expires ?? now.AddMinutes(5)).UtcDateTime,
                SigningCredentials = new SigningCredentials(signingKeys[^1].SecurityKey, SecurityAlgorithms.RsaSha256)
            });
        }
    }

    public byte[] PublicKeyBytes()
    {
        lock (keyLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return signingKeys[^1].Rsa.ExportSubjectPublicKeyInfo();
        }
    }

    public void RotateSigningKey(bool retainPreviousKey = true)
    {
        lock (keyLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var next = SigningKey.Create();
            if (!retainPreviousKey)
            {
                foreach (var previous in signingKeys)
                    previous.Rsa.Dispose();
                signingKeys.Clear();
            }
            signingKeys.Add(next);
        }
    }

    private PublicJwk[] PublicKeys()
    {
        lock (keyLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return signingKeys.Select(key => key.PublicJwk).ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (keyLock)
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var key in signingKeys)
                key.Rsa.Dispose();
            signingKeys.Clear();
        }
        await application.DisposeAsync();
    }

    // This response type has no private RSA fields; export only public parameters.
    private sealed record PublicJwk(string Kty, string Use, string Alg, string Kid, string N, string E);

    private sealed record SigningKey(RSA Rsa, RsaSecurityKey SecurityKey, PublicJwk PublicJwk)
    {
        public static SigningKey Create()
        {
            var rsa = RSA.Create(2048);
            try
            {
                var kid = Guid.NewGuid().ToString("N");
                var securityKey = new RsaSecurityKey(rsa)
                {
                    KeyId = kid,
                    CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
                };
                var parameters = rsa.ExportParameters(includePrivateParameters: false);
                var publicJwk = new PublicJwk("RSA", "sig", "RS256", kid,
                    Base64UrlEncoder.Encode(parameters.Modulus!), Base64UrlEncoder.Encode(parameters.Exponent!));
                return new SigningKey(rsa, securityKey, publicJwk);
            }
            catch
            {
                rsa.Dispose();
                throw;
            }
        }
    }
}
