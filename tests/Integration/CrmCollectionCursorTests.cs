using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Platform.Api.Crm;
using Xunit;

namespace Integration;

public class CrmCollectionCursorTests
{
    private readonly byte[] key = RandomNumberGenerator.GetBytes(32);
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid anchor = Guid.NewGuid();

    [Theory]
    [InlineData("tags")]
    [InlineData("field-definitions")]
    [InlineData("contact-tags")]
    public void Tokens_bind_context_and_anchor(string kind)
    {
        var cursor = new CrmCollectionCursor(key);
        Guid? contact = kind == "contact-tags" ? Guid.NewGuid() : null;
        var token = cursor.Encode(tenant, kind, contact, anchor);
        Assert.True(cursor.TryDecode(token, tenant, kind, contact, out var decoded));
        Assert.Equal(anchor, decoded);
        Assert.False(cursor.TryDecode(token, Guid.NewGuid(), kind, contact, out _));
        Assert.False(cursor.TryDecode(token, tenant, kind == "tags" ? "field-definitions" : "tags", null, out _));
        Assert.False(cursor.TryDecode(token, tenant, kind, Guid.NewGuid(), out _));
        Assert.False(new CrmCollectionCursor(RandomNumberGenerator.GetBytes(32)).TryDecode(token, tenant, kind, contact, out _));
        var bytes = WebEncoders.Base64UrlDecode(token);
        Assert.Equal(49, bytes.Length); // version, anchor, MAC only
        Assert.Equal(anchor, new Guid(bytes.AsSpan(1, 16), bigEndian: true));
        bytes[^1] ^= 1;
        Assert.False(cursor.TryDecode(WebEncoders.Base64UrlEncode(bytes), tenant, kind, contact, out _));
        foreach (var invalid in new[] { "", "!", token + "=", new string('A', 2049) })
            Assert.False(cursor.TryDecode(invalid, tenant, kind, contact, out _));
        var contacts = new ContactCursor(key);
        var query = new ContactListQuery(20, "private query", null, null, null);
        Assert.False(cursor.TryDecode(contacts.Encode(tenant, query, DateTime.UnixEpoch, anchor), tenant, kind, contact, out _));
        Assert.False(contacts.TryDecode(token, tenant, query, out _, out _));
    }

    [Fact]
    public void Noncanonical_alias_and_invalid_collection_context_are_rejected()
    {
        var cursor = new CrmCollectionCursor(key);
        var token = cursor.Encode(tenant, "tags", null, anchor);
        Assert.False(cursor.TryDecode("!" + token[1..], tenant, "tags", null, out _));
        Assert.False(cursor.TryDecode(token[..^1] + "=", tenant, "tags", null, out _));
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var alias = token[..^1] + alphabet[alphabet.IndexOf(token[^1]) + 1];
        Assert.False(cursor.TryDecode(alias, tenant, "tags", null, out _));
        foreach (var (kind, contact) in new (string, Guid?)[]
                 { ("unknown", null), ("contact-tags", null), ("tags", Guid.NewGuid()) })
        {
            Assert.Throws<ArgumentException>(() => cursor.Encode(tenant, kind, contact, anchor));
            Assert.False(cursor.TryDecode(token, tenant, kind, contact, out _));
        }
    }

    [Fact]
    public void Configuration_requires_canonical_existing_key()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        foreach (var invalid in new[] { null, "!", Convert.ToBase64String(key[..31]), Convert.ToBase64String(key) + " " })
        {
            config["ContactCursor:SigningKey"] = invalid;
            Assert.False(CrmCollectionCursor.TryCreate(config, out var missing));
            Assert.Null(missing);
        }
        config["ContactCursor:SigningKey"] = Convert.ToBase64String(key);
        Assert.True(CrmCollectionCursor.TryCreate(config, out var configured));
        Assert.Equal(new CrmCollectionCursor(key).Encode(tenant, "tags", null, anchor), configured!.Encode(tenant, "tags", null, anchor));
    }
}
