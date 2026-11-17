using System.Text;
using Microsoft.Extensions.Primitives;
using Platform.Api.Identity;
using Xunit;

namespace Integration;

public sealed class MembershipParsingTests
{
    [Fact]
    public void Cursor_round_trips_position_only_for_selected_tenant()
    {
        var tenant = Guid.NewGuid();
        var anchor = Guid.NewGuid();
        var encoded = MembershipCursor.Encode(tenant, anchor);
        Assert.True(MembershipCursor.TryDecode(encoded, tenant, out var parsed));
        Assert.Equal(anchor, parsed);
        Assert.False(MembershipCursor.TryDecode(encoded, Guid.NewGuid(), out _));
        Assert.False(encoded.Contains('='));
    }

    [Fact]
    public void Cursor_rejects_noncanonical_malformed_wrong_version_and_oversized_values()
    {
        var tenant = Guid.NewGuid();
        var anchor = Guid.NewGuid();
        static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        foreach (var bad in new[] { "", "!", new string('A', 2049), "A", MembershipCursor.Encode(tenant, anchor) + "=", Encode($"v2:{tenant:D}:{anchor:D}"), Encode($"v1:{tenant:D}:{anchor:D}:extra"), Encode($"v1:{tenant:D}:{anchor:D}".ToUpperInvariant()) })
            Assert.False(MembershipCursor.TryDecode(bad, tenant, out _));
    }

    [Fact]
    public void If_match_requires_one_canonical_strong_version_tag()
    {
        Assert.Equal("\"v1\"", MembershipPreconditions.Format(1));
        Assert.Equal(MembershipPreconditionResult.Missing, MembershipPreconditions.Parse(StringValues.Empty, out _));
        Assert.Equal(MembershipPreconditionResult.Valid, MembershipPreconditions.Parse(new StringValues("\"v123\""), out var version));
        Assert.Equal(123, version);
        foreach (var bad in new[] { "", "*", "W/\"v1\"", "\"v1\",\"v2\"", "v1", "\"v0\"", "\"v01\"", "\"v-1\"", "\"v9223372036854775808\"", " \"v1\"" })
            Assert.Equal(MembershipPreconditionResult.Invalid, MembershipPreconditions.Parse(new StringValues(bad), out _));
        Assert.Equal(MembershipPreconditionResult.Invalid, MembershipPreconditions.Parse(new StringValues(new[] { "\"v1\"", "\"v2\"" }), out _));
    }
}
