using System.Text;

namespace Platform.Api.Identity;

internal static class TenantCursor
{
    public static string Encode(Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes(id.ToString("D")))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string value, out Guid id)
    {
        id = default;
        if (value.Length == 0 || value.Length > 2048 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            return false;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var text = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(base64));
            return Guid.TryParseExact(text, "D", out id) && text == id.ToString("D") && Encode(id) == value;
        }
        catch (FormatException) { return false; }
        catch (DecoderFallbackException) { return false; }
    }

    // UUID ordering in PostgreSQL is bytewise; the function's exclusive bound
    // can locate exactly this UUID by starting at its immediate predecessor.
    public static Guid? Predecessor(Guid id)
    {
        var bytes = Convert.FromHexString(id.ToString("N"));
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            if (bytes[i] != 0)
            {
                bytes[i]--;
                return Guid.ParseExact(Convert.ToHexString(bytes), "N");
            }
            bytes[i] = 255;
        }
        return null;
    }
}
