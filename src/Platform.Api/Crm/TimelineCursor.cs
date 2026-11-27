using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Platform.Api.Crm;

public sealed class TimelineCursor
{
    private const int PayloadLength = 57; // version, timestamp, id, tenant/contact fingerprint
    private const int TokenLength = PayloadLength + 32;
    private readonly byte[] key;

    public TimelineCursor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32) throw new ArgumentException("Cursor signing key must be at least 32 bytes.", nameof(key));
        this.key = key.ToArray();
    }

    public static bool TryCreate(IConfiguration configuration, out TimelineCursor? cursor)
        => CursorSigningKey.TryCreate(configuration, key => new TimelineCursor(key), out cursor);

    public string Encode(Guid tenant, Guid contactId, DateTime occurredAt, Guid id)
    {
        if (occurredAt.Kind != DateTimeKind.Utc || occurredAt.Ticks % 10 != 0) throw new ArgumentException("Anchor must be UTC with microsecond precision.", nameof(occurredAt));
        var bytes = new byte[TokenLength];
        bytes[0] = 1;
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(1, 8), occurredAt.Ticks);
        id.TryWriteBytes(bytes.AsSpan(9, 16), bigEndian: true, out _);
        Fingerprint(tenant, contactId).CopyTo(bytes, 25);
        HMACSHA256.HashData(key, bytes.AsSpan(0, PayloadLength), bytes.AsSpan(PayloadLength));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public bool TryDecode(string token, Guid tenant, Guid contactId, out DateTime occurredAt, out Guid id)
    {
        occurredAt = default; id = default;
        if (token?.Length != (TokenLength * 8 + 5) / 6) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(token); }
        catch (FormatException) { return false; }
        if (bytes.Length != TokenLength || WebEncoders.Base64UrlEncode(bytes) != token) return false;
        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(key, bytes.AsSpan(0, PayloadLength), expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes.AsSpan(PayloadLength))) return false;
        if (bytes[0] != 1 || !CryptographicOperations.FixedTimeEquals(Fingerprint(tenant, contactId), bytes.AsSpan(25, 32))) return false;
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(1, 8));
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || ticks % 10 != 0) return false;
        occurredAt = new DateTime(ticks, DateTimeKind.Utc);
        id = new Guid(bytes.AsSpan(9, 16), bigEndian: true);
        return true;
    }

    private byte[] Fingerprint(Guid tenant, Guid contactId)
    {
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        writer.Write("crm-timeline-v1:occurred_at-desc,id-desc");
        writer.Write(tenant.ToByteArray());
        writer.Write(contactId.ToByteArray());
        writer.Flush();
        return HMACSHA256.HashData(key, data.GetBuffer().AsSpan(0, checked((int)data.Length)));
    }
}
