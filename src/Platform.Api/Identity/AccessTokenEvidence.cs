using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Api.Identity;

internal sealed class RequireAccessToken;

public static class AccessTokenEvidence
{
    // The handler flattens JSON arrays and can collapse duplicate object members.
    // Inspect the signed payload after bearer validation, while those distinctions still exist.
    public static bool ValidatePurposeAndFilterEmail(ClaimsPrincipal principal, SecurityToken token)
    {
        var isAccessToken = false;
        if (token is JsonWebToken jwt)
        {
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
            var tokenTypes = 0;
            var emails = 0;
            var verified = 0;
            var validEmail = true;
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (property.NameEquals("typ"))
                {
                    tokenTypes++;
                    isAccessToken = property.Value.ValueKind == JsonValueKind.String && property.Value.ValueEquals("Bearer");
                }
                else if (property.NameEquals("email"))
                {
                    emails++;
                    validEmail &= property.Value.ValueKind == JsonValueKind.String;
                }
                else if (property.NameEquals("email_verified"))
                {
                    verified++;
                    validEmail &= property.Value.ValueKind == JsonValueKind.True;
                }
            }
            isAccessToken &= tokenTypes == 1;
            if (isAccessToken && validEmail && emails == 1 && verified == 1)
                return true;
        }

        foreach (var identity in principal.Identities)
            foreach (var claim in identity.FindAll("email_verified").ToArray())
                identity.RemoveClaim(claim);
        return isAccessToken;
    }
}
