using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Configuration;
using Platform.Api.Crm;
using Xunit;

namespace Integration;

public class ContactCursorTests
{
    private static IQueryCollection Query(params (string, string[])[] fields) =>
        new QueryCollection(fields.ToDictionary(x => x.Item1, x => new StringValues(x.Item2)));
    private static ContactListQuery Parse(params (string, string[])[] fields)
    {
        Assert.True(ContactListQuery.TryParse(Query(fields), out var q));
        return Assert.IsType<ContactListQuery>(q);
    }
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Anchor = Guid.NewGuid();
    private static readonly DateTime Stamp = new(2026, 11, 27, 1, 2, 3, DateTimeKind.Utc);

    [Fact]
    public void Defaults_and_valid_filters_are_preserved()
    {
        Assert.Equal(20, Parse().Limit);
        var tag = Guid.NewGuid();
        var q = Parse(("limit", ["100"]), ("q", ["%_\\猫😀"]), ("tagId", [tag.ToString("D")]), ("updatedSince", ["2026-11-27T08:02:03.0000001+07:00"]));
        Assert.Equal(100, q.Limit);
        Assert.Equal("%_\\猫😀", q.Q);
        Assert.Equal(tag, q.TagId);
        Assert.Equal(new DateTimeOffset(Stamp.AddTicks(10)), q.UpdatedSince);
    }

    [Theory]
    [InlineData("limit", "0")][InlineData("limit", "101")][InlineData("limit", "01")][InlineData("limit", "+1")]
    [InlineData("q", "")][InlineData("q", "a\0b")][InlineData("tagId", "INVALID")]
    [InlineData("updatedSince", "2026-11-27T01:02:03")][InlineData("updatedSince", "nonsense")]
    [InlineData("unknown", "value")]
    public void Invalid_query_is_rejected(string name, string value) => Assert.False(ContactListQuery.TryParse(Query((name, [value])), out _));

    [Fact]
    public void Repeated_values_invalid_unicode_and_bounds_are_rejected()
    {
        Assert.False(ContactListQuery.TryParse(Query(("q", ["a", "b"])), out _));
        Assert.False(ContactListQuery.TryParse(Query(("q", [new string('a', 201)])), out _));
        Assert.False(ContactListQuery.TryParse(Query(("q", ["\ud800"])), out _));
        Assert.False(ContactListQuery.TryParse(Query(("cursor", [new string('a', 2049)])), out _));
        var upper = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
        Assert.False(ContactListQuery.TryParse(Query(("tagId", [upper])), out _));
    }

    [Fact]
    public void Valid_timestamp_with_long_fraction_rounds_up_to_microsecond()
    {
        var timestamp = "2026-11-27T01:02:03." + new string('0', 2048) + "1Z";
        Assert.Equal(new DateTimeOffset(Stamp.AddTicks(10)), Parse(("updatedSince", [timestamp])).UpdatedSince);
    }

    [Fact]
    public void Cursor_is_bound_to_tenant_filters_and_not_page_size()
    {
        var first = new ContactCursor(Key);
        var second = new ContactCursor(Key.ToArray());
        var q = Parse(("q", ["sensitive cat"]), ("limit", ["1"]));
        var token = first.Encode(Tenant, q, Stamp, Anchor);
        Assert.DoesNotContain("sensitive cat", Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)));
        Assert.True(second.TryDecode(token, Tenant, Parse(("q", ["sensitive cat"]), ("limit", ["99"])), out var timestamp, out var id));
        Assert.Equal(Stamp, timestamp);
        Assert.Equal(Anchor, id);
        Assert.False(second.TryDecode(token, Guid.NewGuid(), q, out _, out _));
        Assert.False(second.TryDecode(token, Tenant, Parse(("q", ["other"])), out _, out _));
        Assert.False(new ContactCursor(RandomNumberGenerator.GetBytes(32)).TryDecode(token, Tenant, q, out _, out _));
    }

    [Fact]
    public void Configuration_fails_closed_and_does_not_expose_key()
    {
        IConfiguration Config(string? value) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ContactCursor:SigningKey"] = value }).Build();
        Assert.False(ContactCursor.TryCreate(Config(null), out _));
        Assert.False(ContactCursor.TryCreate(Config("not base64"), out _));
        Assert.False(ContactCursor.TryCreate(Config(Convert.ToBase64String(new byte[31])), out _));
        Assert.True(ContactCursor.TryCreate(Config(Convert.ToBase64String(Key)), out var cursor));
        Assert.NotNull(cursor);
    }

    [Fact]
    public void Signed_unknown_version_and_submicrosecond_anchor_are_rejected()
    {
        var cursor = new ContactCursor(Key);
        var q = Parse();
        var bytes = WebEncoders.Base64UrlDecode(cursor.Encode(Tenant, q, Stamp, Anchor));
        bytes[0] = 2;
        HMACSHA256.HashData(Key, bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32));
        Assert.False(cursor.TryDecode(WebEncoders.Base64UrlEncode(bytes), Tenant, q, out _, out _));
        bytes[0] = 1;
        bytes[8] |= 1;
        HMACSHA256.HashData(Key, bytes.AsSpan(0, bytes.Length - 32), bytes.AsSpan(bytes.Length - 32));
        Assert.False(cursor.TryDecode(WebEncoders.Base64UrlEncode(bytes), Tenant, q, out _, out _));
    }

    [Fact]
    public void Filter_timestamp_is_canonicalized_and_other_filters_bind_cursor()
    {
        var cursor = new ContactCursor(Key);
        var tag = Guid.NewGuid().ToString("D");
        var original = Parse(("tagId", [tag]), ("updatedSince", ["2026-11-27T08:02:03.000000100+07:00"]));
        var equivalent = Parse(("tagId", [tag]), ("updatedSince", ["2026-11-27T01:02:03.000001Z"]));
        var token = cursor.Encode(Tenant, original, Stamp, Anchor);
        Assert.True(cursor.TryDecode(token, Tenant, equivalent, out _, out _));
        Assert.False(cursor.TryDecode(token, Tenant, Parse(("tagId", [tag])), out _, out _));
        Assert.False(cursor.TryDecode(token, Tenant, Parse(("updatedSince", ["2026-11-27T01:02:03.000001Z"])), out _, out _));
    }

    [Fact]
    public void Cursor_rejects_tampering_noncanonical_encoding_and_bad_lengths()
    {
        var cursor = new ContactCursor(Key);
        var q = Parse();
        var token = cursor.Encode(Tenant, q, Stamp, Anchor);
        var bytes = WebEncoders.Base64UrlDecode(token);
        for (var i = 0; i < bytes.Length; i++)
        {
            var changed = bytes.ToArray(); changed[i] ^= 1;
            Assert.False(cursor.TryDecode(WebEncoders.Base64UrlEncode(changed), Tenant, q, out _, out _));
        }
        Assert.False(cursor.TryDecode(token + "=", Tenant, q, out _, out _));
        Assert.False(cursor.TryDecode("*", Tenant, q, out _, out _));
        Assert.False(cursor.TryDecode(new string('a', 2049), Tenant, q, out _, out _));
        Assert.Throws<ArgumentException>(() => cursor.Encode(Tenant, q, Stamp.AddTicks(1), Anchor));
        Assert.Throws<ArgumentException>(() => new ContactCursor(new byte[31]));
    }
}
