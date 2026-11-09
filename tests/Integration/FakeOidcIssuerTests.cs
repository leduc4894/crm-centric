using System.Net;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration;

public sealed class FakeOidcIssuerTests
{
    [Fact]
    public async Task Discovery_and_jwks_match_bound_issuer()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        Assert.Equal("http", issuer.Issuer.Scheme);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(issuer.Issuer.Host)));
        Assert.NotEqual(0, issuer.Issuer.Port);

        using var response = await client.GetAsync(new Uri(issuer.Issuer, ".well-known/openid-configuration"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var discovery = document.RootElement;
        Assert.True(discovery.TryGetProperty("issuer", out _));
        Assert.Equal(issuer.Issuer.AbsoluteUri, discovery.GetProperty("issuer").GetString());
        Assert.Equal(issuer.JwksUri.AbsoluteUri, discovery.GetProperty("jwks_uri").GetString());

        var publicJwk = Assert.Single(await ReadKeysAsync(client, issuer));
        Assert.Equal("RSA", publicJwk.GetProperty("kty").GetString());
        Assert.Equal("RS256", publicJwk.GetProperty("alg").GetString());
        Assert.Equal("sig", publicJwk.GetProperty("use").GetString());
        Assert.False(string.IsNullOrWhiteSpace(publicJwk.GetProperty("kid").GetString()));
        Assert.True(Base64UrlEncoder.DecodeBytes(publicJwk.GetProperty("n").GetString()!).Length >= 256);
        Assert.NotEmpty(Base64UrlEncoder.DecodeBytes(publicJwk.GetProperty("e").GetString()!));
        Assert.False(publicJwk.TryGetProperty("d", out _));
    }

    [Fact]
    public async Task Token_signature_verifies_using_public_jwks()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        var encoded = issuer.IssueToken(new FakeTokenOptions());
        Assert.NotEmpty(encoded);
        var token = new JsonWebToken(encoded);
        Assert.Equal("RS256", token.Alg);
        Assert.Equal("fixture-user", token.Subject);
        Assert.Equal("crm-api", Assert.Single(token.Audiences));
        Assert.Equal(issuer.Issuer.AbsoluteUri, token.Issuer);
        Assert.InRange(token.IssuedAt, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow);
        Assert.InRange(token.ValidTo, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10));
        var keys = await ReadKeysAsync(client, issuer);
        Assert.Equal(keys.Single().GetProperty("kid").GetString(), token.Kid);
        var validation = await ValidateAsync(encoded, issuer, keys);
        Assert.True(validation.IsValid);

        // A genuine signature must not validate against another fixture's public key.
        await using var otherIssuer = await FakeOidcIssuer.StartAsync();
        var wrongKeyValidation = await ValidateAsync(encoded, issuer, await ReadKeysAsync(client, otherIssuer));
        Assert.False(wrongKeyValidation.IsValid);
    }

    [Fact]
    public async Task Rotation_keeps_or_retires_previous_kid()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        var first = issuer.IssueToken(new FakeTokenOptions());
        Assert.NotEmpty(first);
        var firstKid = new JsonWebToken(first).Kid;
        issuer.RotateSigningKey();
        var second = issuer.IssueToken(new FakeTokenOptions());
        var secondKid = new JsonWebToken(second).Kid;
        Assert.NotEqual(firstKid, secondKid);
        var retained = await ReadKeysAsync(client, issuer);
        Assert.Equal(2, retained.Length);
        Assert.True((await ValidateAsync(first, issuer, retained)).IsValid);
        Assert.True((await ValidateAsync(second, issuer, retained)).IsValid);

        issuer.RotateSigningKey(retainPreviousKey: false);
        var third = issuer.IssueToken(new FakeTokenOptions());
        var thirdKid = new JsonWebToken(third).Kid;
        Assert.NotEqual(firstKid, thirdKid);
        Assert.NotEqual(secondKid, thirdKid);
        var retired = await ReadKeysAsync(client, issuer);
        Assert.Equal(thirdKid, Assert.Single(retired).GetProperty("kid").GetString());
        Assert.False((await ValidateAsync(first, issuer, retired)).IsValid);
        Assert.False((await ValidateAsync(second, issuer, retired)).IsValid);
        Assert.True((await ValidateAsync(third, issuer, retired)).IsValid);
    }

    [Fact]
    public async Task Fixture_publishes_no_private_key_or_credentials()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        var publicJwk = Assert.Single(await ReadKeysAsync(client, issuer));
        // An allowlist also rejects accidental private RSA fields, x5c or credentials.
        Assert.Equal(new[] { "alg", "e", "kid", "kty", "n", "use" },
            publicJwk.EnumerateObject().Select(property => property.Name).Order().ToArray());
        foreach (var privateField in new[] { "d", "p", "q", "dp", "dq", "qi", "oth", "k" })
            Assert.False(publicJwk.TryGetProperty(privateField, out _));

        using var discoveryResponse = await client.GetAsync(new Uri(issuer.Issuer, ".well-known/openid-configuration"));
        using var discovery = JsonDocument.Parse(await discoveryResponse.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "issuer", "jwks_uri" },
            discovery.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        foreach (var route in new[] { "token", "authorize", "connect/token", "connect/authorize" })
        {
            using var get = await client.GetAsync(new Uri(issuer.Issuer, route));
            using var post = await client.PostAsync(new Uri(issuer.Issuer, route), new StringContent(""));
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("not-yet-valid")]
    public async Task Options_express_invalid_tokens_with_standard_validation(string scenario)
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        var now = DateTimeOffset.UtcNow;
        var options = new FakeTokenOptions
        {
            IssuerOverride = scenario == "issuer" ? "https://wrong-issuer.invalid/" : null,
            Audience = scenario == "audience" ? "other-api" : "crm-api",
            Subject = "custom-subject",
            IssuedAt = now.AddHours(-2),
            NotBefore = scenario == "not-yet-valid" ? now.AddHours(1) : now.AddHours(-2),
            Expires = scenario == "expired" ? now.AddHours(-1) : now.AddHours(2)
        };
        var encoded = issuer.IssueToken(options);
        Assert.NotEmpty(encoded);
        var token = new JsonWebToken(encoded);
        Assert.Equal("custom-subject", token.Subject);
        Assert.Equal(options.IssuedAt.Value.ToUnixTimeSeconds(), new DateTimeOffset(token.IssuedAt).ToUnixTimeSeconds());
        Assert.Equal(options.NotBefore.Value.ToUnixTimeSeconds(), new DateTimeOffset(token.ValidFrom).ToUnixTimeSeconds());
        Assert.Equal(options.Expires.Value.ToUnixTimeSeconds(), new DateTimeOffset(token.ValidTo).ToUnixTimeSeconds());
        var result = await ValidateAsync(encoded, issuer, await ReadKeysAsync(client, issuer));
        Assert.False(result.IsValid);
        switch (scenario)
        {
            case "expired": Assert.IsType<SecurityTokenExpiredException>(result.Exception); break;
            case "issuer": Assert.IsType<SecurityTokenInvalidIssuerException>(result.Exception); break;
            case "audience": Assert.IsType<SecurityTokenInvalidAudienceException>(result.Exception); break;
            case "not-yet-valid": Assert.IsType<SecurityTokenNotYetValidException>(result.Exception); break;
        }
    }

    [Fact]
    public async Task Concurrent_signing_rotation_and_jwks_reads_keep_valid_snapshots()
    {
        await using var issuer = await FakeOidcIssuer.StartAsync();
        using var client = new HttpClient();
        var batches = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            issuer.RotateSigningKey();
            var encoded = issuer.IssueToken(new FakeTokenOptions());
            Assert.NotEmpty(encoded);
            var keys = await ReadKeysAsync(client, issuer);
            Assert.True((await ValidateAsync(encoded, issuer, keys)).IsValid);
            return encoded;
        })));
        var finalKeys = await ReadKeysAsync(client, issuer);
        Assert.Equal(9, finalKeys.Length);
        Assert.Equal(9, finalKeys.Select(key => key.GetProperty("kid").GetString()).Distinct().Count());
        foreach (var encoded in batches)
            Assert.True((await ValidateAsync(encoded, issuer, finalKeys)).IsValid);
    }

    private static async Task<JsonElement[]> ReadKeysAsync(HttpClient client, FakeOidcIssuer issuer)
    {
        using var response = await client.GetAsync(issuer.JwksUri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.Clone()).ToArray();
    }

    private static Task<TokenValidationResult> ValidateAsync(string encoded, FakeOidcIssuer issuer, JsonElement[] keys) =>
        new JsonWebTokenHandler().ValidateTokenAsync(encoded, new TokenValidationParameters
        {
            ValidIssuer = issuer.Issuer.AbsoluteUri,
            ValidAudience = "crm-api",
            IssuerSigningKeys = keys.Select(key => new JsonWebKey(key.GetRawText())),
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero
        });
}
