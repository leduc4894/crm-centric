using System.Text;

namespace Platform.Api.Identity;

public static class MembershipCursor
{
    public static string Encode(Guid tenantId, Guid membershipId) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"v1:{tenantId:D}:{membershipId:D}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string value, Guid tenantId, out Guid membershipId)
    {
        membershipId = default;
        if (string.IsNullOrEmpty(value) || value.Length > 2048 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
            var parts = text.Split(':');
            if (parts.Length != 3 || parts[0] != "v1" ||
                !Guid.TryParseExact(parts[1], "D", out var cursorTenant) || cursorTenant != tenantId ||
                !Guid.TryParseExact(parts[2], "D", out var anchor) || Encode(cursorTenant, anchor) != value)
                return false;
            membershipId = anchor;
            return true;
        }
        catch (FormatException) { return false; }
        catch (DecoderFallbackException) { return false; }
    }
}
