using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using Platform.Api.Identity;
using Xunit;

namespace Integration;

public sealed class InviteSecurityTests
{
    [Fact]
    public void Token_is_random_canonical_and_round_trips_to_digest()
    {
        var (token, hash) = InviteToken.Create();
        var (other, _) = InviteToken.Create();
        Assert.Equal(43, token.Length);
        Assert.Equal(32, hash.Length);
        Assert.False(string.Equals(token, other, StringComparison.Ordinal));
        Assert.True(InviteToken.TryHash(token, out var actual));
        Assert.True(CryptographicOperations.FixedTimeEquals(hash, actual));
        var decoded = Base64UrlEncoder.DecodeBytes(token);
        Assert.Equal(32, decoded.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(SHA256.HashData(decoded), hash));
        foreach (var invalid in new[] { "", token + "=", token[..42], new string('!', 43), new string('A', 42) + "B" })
            Assert.False(InviteToken.TryHash(invalid, out _));
    }

    [Fact]
    public void Email_normalizes_domain_only_and_rejects_unsafe_values()
    {
        Assert.True(InviteEmail.TryNormalize("Alice@Example.COM", out var email));
        Assert.Equal("Alice@example.com", email);
        foreach (var invalid in new[] { "", "a @example.com", "a@@example.com", "a@example.com\n", "á@example.com", "a@-bad.com", "a@example..com", new string('a', 321) })
            Assert.False(InviteEmail.TryNormalize(invalid, out _));
    }

    [Fact]
    public void Verified_email_requires_unique_boolean_true_claim()
    {
        static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));
        var email = new Claim("email", "Alice@Example.COM");
        var yes = new Claim("email_verified", "true", ClaimValueTypes.Boolean);
        Assert.True(VerifiedEmail.TryGet(Principal(email, yes), out var normalized));
        Assert.Equal("Alice@example.com", normalized);
        Assert.False(VerifiedEmail.TryGet(Principal(email), out _));
        Assert.False(VerifiedEmail.TryGet(Principal(email, new Claim("email_verified", "true")), out _));
        Assert.False(VerifiedEmail.TryGet(Principal(email, new Claim("email_verified", "false", ClaimValueTypes.Boolean)), out _));
        Assert.False(VerifiedEmail.TryGet(Principal(email, email, yes), out _));
        Assert.False(VerifiedEmail.TryGet(Principal(email, yes, yes), out _));
    }
    [Theory]
    [InlineData("\"email\":\"Alice@Example.COM\",\"email_verified\":true", true)]
    [InlineData("\"email\":\"Alice@Example.COM\",\"email_verified\":\"true\"", false)]
    [InlineData("\"email\":[\"Alice@Example.COM\"],\"email_verified\":true", false)]
    [InlineData("\"email\":\"Alice@Example.COM\",\"email_verified\":[true]", false)]
    [InlineData("\"email\":\"Alice@Example.COM\",\"email_verified\":false,\"email_verified\":true", false)]
    [InlineData("\"email\":\"Other@example.com\",\"email\":\"Alice@Example.COM\",\"email_verified\":true", false)]
    [InlineData("\"email\":\"Alice@Example.COM\",\"email_verified\":true,\"typ\":\"Bearer\"", false)]
    public async Task Validated_JWT_preserves_scalar_types_and_duplicate_members(string fields, bool expected)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa);
        var handler = new JsonWebTokenHandler { MapInboundClaims = false, SetDefaultTimesOnTokenCreation = false };
        var payload = "{\"typ\":\"Bearer\",\"iss\":\"issuer\",\"aud\":\"api\",\"exp\":" +
            DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() + "," + fields + "}";
        var encoded = handler.CreateToken(payload, new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var result = await handler.ValidateTokenAsync(encoded, new TokenValidationParameters
        {
            ValidIssuer = "issuer", ValidAudience = "api", IssuerSigningKey = key
        });
        Assert.True(result.IsValid);
        var principal = new ClaimsPrincipal(result.ClaimsIdentity);
        AccessTokenEvidence.ValidatePurposeAndFilterEmail(principal, result.SecurityToken);
        Assert.Equal(expected, VerifiedEmail.TryGet(principal, out _));
    }
}
