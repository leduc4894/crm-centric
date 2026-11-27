using System.Buffers.Binary;
using Microsoft.AspNetCore.WebUtilities;

namespace Platform.Api.Identity;

internal static class AuditCursor
{
    // Version byte, tenant UUID, UTC ticks (big endian), entry UUID.
    public static string Encode(Guid tenant, DateTime timestamp, Guid id)
    {
        var bytes = new byte[41];
        bytes[0] = 1;
        tenant.TryWriteBytes(bytes.AsSpan(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(17, 8), timestamp.Ticks);
        id.TryWriteBytes(bytes.AsSpan(25, 16));
        return WebEncoders.Base64UrlEncode(bytes);
    }

    public static bool TryDecode(string cursor, Guid tenant, out DateTime timestamp, out Guid id)
    {
        timestamp = default;
        id = default;
        if (cursor.Length != 55) return false;
        byte[] bytes;
        try { bytes = WebEncoders.Base64UrlDecode(cursor); }
        catch (FormatException) { return false; }
        if (bytes.Length != 41 || bytes[0] != 1 || new Guid(bytes.AsSpan(1, 16)) != tenant) return false;
        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(17, 8));
        if (ticks <= 0 || ticks >= DateTime.MaxValue.Ticks || ticks % 10 != 0) return false;
        timestamp = new DateTime(ticks, DateTimeKind.Utc);
        id = new Guid(bytes.AsSpan(25, 16));
        return cursor == Encode(tenant, timestamp, id);
    }
}
