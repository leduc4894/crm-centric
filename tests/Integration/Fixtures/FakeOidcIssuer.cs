using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    public string Audience { get; init; } = "crm-api";
    public string Subject { get; init; } = "fixture-user";
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

    private FakeOidcIssuer(WebApplication application)
    {
        this.application = application;
        signingKeys.Add(SigningKey.Create());
    }

    public Uri Issuer { get; private set; } = null!;
    public Uri JwksUri => new(Issuer, ".well-known/jwks.json");

    public static async Task<FakeOidcIssuer> StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        var application = builder.Build();
        FakeOidcIssuer? fixture = null;
        try
        {
            fixture = new FakeOidcIssuer(application);
            application.MapGet("/.well-known/openid-configuration", () => new
            {
                issuer = fixture.Issuer.AbsoluteUri,
                jwks_uri = fixture.JwksUri.AbsoluteUri
            });
            application.MapGet("/.well-known/jwks.json", () => new { keys = fixture.PublicKeys() });
            await application.StartAsync(cancellationToken);
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!;
            fixture.Issuer = new Uri(addresses.Addresses.Single());
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
            return tokenHandler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = options.IssuerOverride ?? Issuer.AbsoluteUri,
                Audience = options.Audience,
                Claims = new Dictionary<string, object> { ["sub"] = options.Subject },
                IssuedAt = (options.IssuedAt ?? now.AddMinutes(-1)).UtcDateTime,
                NotBefore = (options.NotBefore ?? now.AddMinutes(-1)).UtcDateTime,
                Expires = (options.Expires ?? now.AddMinutes(5)).UtcDateTime,
                SigningCredentials = new SigningCredentials(signingKeys[^1].SecurityKey, SecurityAlgorithms.RsaSha256)
            });
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
