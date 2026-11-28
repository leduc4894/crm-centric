using System.Security.Cryptography;

namespace Platform.Api.Crm;

internal static class CursorSigningKey
{
    internal static bool TryCreate<T>(IConfiguration configuration, Func<byte[], T> factory, out T? cursor) where T : class
    {
        cursor = null;
        var configured = configuration["ContactCursor:SigningKey"];
        if (string.IsNullOrEmpty(configured)) return false;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(configured); }
        catch (FormatException) { return false; }
        try
        {
            if (bytes.Length < 32 || Convert.ToBase64String(bytes) != configured) return false;
            cursor = factory(bytes);
            return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
