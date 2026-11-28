using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Platform.Api.Crm;

public sealed class CrmCollectionCursor
{
    private const int PayloadLength = 17; // version and last UUID
    private const int TokenLength = PayloadLength + 32;
    private const int EncodedLength = (TokenLength * 8 + 5) / 6;
    private readonly byte[] key;

    public CrmCollectionCursor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32) throw new ArgumentException("Cursor signing key must be at least 32 bytes.", nameof(key));
        this.key = key.ToArray();
    }

    public static bool TryCreate(IConfiguration configuration, out CrmCollectionCursor? cursor)
        => CursorSigningKey.TryCreate(configuration, key => new CrmCollectionCursor(key), out cursor);

    public string Encode(Guid tenant, string kind, Guid? contactId, Guid anchor)
    {
        if (!ValidContext(kind, contactId)) throw new ArgumentException("Invalid collection context.", nameof(kind));
        var bytes = new byte[TokenLength];
        bytes[0] = 1;
        anchor.TryWriteBytes(bytes.AsSpan(1, 16), bigEndian: true, out _);
        Sign(tenant, kind, contactId, bytes.AsSpan(0, PayloadLength)).CopyTo(bytes, PayloadLength);
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public bool TryDecode(string token, Guid tenant, string kind, Guid? contactId, out Guid anchor)
    {
        anchor = default;
        if (token?.Length != EncodedLength || !ValidContext(kind, contactId)) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(token); }
        catch (FormatException) { return false; }
        if (bytes.Length != TokenLength || bytes[0] != 1 || WebEncoders.Base64UrlEncode(bytes) != token) return false;
        var expected = Sign(tenant, kind, contactId, bytes.AsSpan(0, PayloadLength));
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(PayloadLength))) return false;
        anchor = new Guid(bytes.AsSpan(1, 16), bigEndian: true);
        return true;
    }

    private static bool ValidContext(string kind, Guid? contactId) => kind switch
    {
        "contact-tags" => contactId.HasValue,
        "tags" or "field-definitions" => !contactId.HasValue,
        _ => false
    };

    private byte[] Sign(Guid tenant, string kind, Guid? contactId, ReadOnlySpan<byte> payload)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        writer.Write("crm-collection-cursor-v1:id-asc");
        writer.Write(kind);
        writer.Write(tenant.ToByteArray());
        if (contactId.HasValue) writer.Write(contactId.Value.ToByteArray());
        writer.Write(payload);
        return HMACSHA256.HashData(key, data.GetBuffer().AsSpan(0, checked((int)data.Length)));
    }
}
