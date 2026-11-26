using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Api.Identity;

public static class VerifiedEmail
{
    // The handler flattens JSON arrays and can collapse duplicate object members.
    // Inspect the signed payload after bearer validation, while those distinctions still exist.
    public static void ApplyValidatedPayload(ClaimsPrincipal principal, SecurityToken token)
    {
        if (token is JsonWebToken jwt)
        {
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(jwt.EncodedPayload));
            var emails = 0;
            var verified = 0;
            var valid = true;
            foreach (var property in payload.RootElement.EnumerateObject())
            {
                if (property.NameEquals("email"))
                {
                    emails++;
                    valid &= property.Value.ValueKind == JsonValueKind.String;
                }
                else if (property.NameEquals("email_verified"))
                {
                    verified++;
                    valid &= property.Value.ValueKind == JsonValueKind.True;
                }
            }
            if (valid && emails == 1 && verified == 1)
                return;
        }

        foreach (var identity in principal.Identities)
            foreach (var claim in identity.FindAll("email_verified").ToArray())
                identity.RemoveClaim(claim);
    }

    public static bool TryGet(ClaimsPrincipal principal, out string normalized)
    {
        normalized = string.Empty;
        var emails = principal.Claims.Where(c => c.Type == "email").ToArray();
        var verified = principal.Claims.Where(c => c.Type == "email_verified").ToArray();
        return emails.Length == 1 && emails[0].ValueType == ClaimValueTypes.String &&
            verified.Length == 1 && verified[0].Subject == emails[0].Subject &&
            verified[0].ValueType == ClaimValueTypes.Boolean &&
            string.Equals(verified[0].Value, "true", StringComparison.OrdinalIgnoreCase) &&
            InviteEmail.TryNormalize(emails[0].Value, out normalized);
    }
}
