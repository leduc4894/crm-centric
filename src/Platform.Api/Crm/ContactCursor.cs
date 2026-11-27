using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Platform.Api.Crm;

public sealed class ContactCursor
{
    private const int PayloadLength = 57; // version, timestamp, id, filter fingerprint
    private const int TokenLength = PayloadLength + 32;
    private readonly byte[] key;

    public ContactCursor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32) throw new ArgumentException("Cursor signing key must be at least 32 bytes.", nameof(key));
        this.key = key.ToArray();
    }

    public static bool TryCreate(IConfiguration configuration, out ContactCursor? cursor)
    {
        cursor = null;
        var configured = configuration["ContactCursor:SigningKey"];
        if (string.IsNullOrEmpty(configured)) return false;
        try
        {
            var bytes = Convert.FromBase64String(configured);
            if (bytes.Length < 32 || Convert.ToBase64String(bytes) != configured) return false;
            cursor = new ContactCursor(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public string Encode(Guid tenant, ContactListQuery query, DateTime updatedAt, Guid id)
    {
        if (updatedAt.Kind != DateTimeKind.Utc || updatedAt.Ticks % 10 != 0) throw new ArgumentException("Anchor must be UTC with microsecond precision.", nameof(updatedAt));
        var bytes = new byte[TokenLength];
        bytes[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1, 8), updatedAt.Ticks);
        id.TryWriteBytes(bytes.AsSpan(9, 16), bigEndian: true, out _);
        Fingerprint(tenant, query).CopyTo(bytes, 25);
        HMACSHA256.HashData(key, bytes.AsSpan(0, PayloadLength), bytes.AsSpan(PayloadLength));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public bool TryDecode(string cursor, Guid tenant, ContactListQuery query, out DateTime updatedAt, out Guid id)
    {
        updatedAt = default; id = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 2048) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(cursor); }
        catch (FormatException) { return false; }
        if (bytes.Length != TokenLength || WebEncoders.Base64UrlEncode(bytes) != cursor) return false;
        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(key, bytes.AsSpan(0, PayloadLength), expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(PayloadLength))) return false;
        if (bytes[0] != 1 || !CryptographicOperations.FixedTimeEquals(Fingerprint(tenant, query), bytes.AsSpan(25, 32))) return false;
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(1, 8));
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || ticks % 10 != 0) return false;
        updatedAt = new DateTime(ticks, DateTimeKind.Utc);
        id = new Guid(bytes.AsSpan(9, 16), bigEndian: true);
        return true;
    }

    private byte[] Fingerprint(Guid tenant, ContactListQuery query)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        writer.Write("contact-list-filter-v1:updated_at-desc,id-desc");
        writer.Write(tenant.ToByteArray());
        writer.Write(query.Q is not null);
        if (query.Q is not null) writer.Write(query.Q);
        writer.Write(query.TagId.HasValue);
        if (query.TagId.HasValue) writer.Write(query.TagId.Value.ToByteArray());
        writer.Write(query.UpdatedSince.HasValue);
        if (query.UpdatedSince.HasValue) writer.Write(query.UpdatedSince.Value.UtcTicks);
        writer.Flush();
        return HMACSHA256.HashData(key, data.GetBuffer().AsSpan(0, checked((int)data.Length)));
    }
}
