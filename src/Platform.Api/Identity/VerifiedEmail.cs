using System.Security.Claims;

namespace Platform.Api.Identity;

public static class VerifiedEmail
{
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
