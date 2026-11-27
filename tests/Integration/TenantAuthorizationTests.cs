using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Xunit;
using Platform.Api.Identity;

namespace Integration;

public sealed class TenantAuthorizationTests
{
    private static ClaimsPrincipal Principal(params string[] scopes) => new(new ClaimsIdentity(scopes.Select(s => new Claim("scope", s)), "test"));

    [Theory]
    [InlineData("owner", true)]
    [InlineData("admin", true)]
    [InlineData("agent", false)]
    [InlineData("operator", false)]
    [InlineData("integration-service", false)]
    [InlineData("OWNER", false)]
    public void Team_manage_is_derived_only_from_staff_membership_role(string role, bool allowed) => Assert.Equal(allowed, TenantAuthorization.HasTeamManage(role));

    [Fact]
    public void Provisioning_scope_requires_one_well_formed_exact_scope_claim()
    {
        Assert.True(TenantAuthorization.HasProvisioningScope(Principal("openid platform.tenants.create profile")));
        foreach (var scopes in new[] { Array.Empty<string>(), ["platform.tenants.create.extra"], ["Platform.tenants.create"], ["platform.tenants.create", "openid"], ["platform.tenants.create  openid"], [" platform.tenants.create"], ["platform.tenants.create\topenid"], ["platform.tenants.create \"bad\""] })
            Assert.False(TenantAuthorization.HasProvisioningScope(Principal(scopes)));
    }

    [Fact]
    public void Tenant_header_must_be_a_single_canonical_uuid()
    {
        var headers = new HeaderDictionary();
        Assert.False(TenantAuthorization.TryGetTenantId(headers, out _));
        var id = Guid.NewGuid();
        headers["X-Tenant-Id"] = id.ToString();
        Assert.True(TenantAuthorization.TryGetTenantId(headers, out var parsed));
        Assert.Equal(id, parsed);
        foreach (var bad in new[] { id.ToString("N"), "not-a-uuid", id.ToString().ToUpperInvariant() })
        {
            headers["X-Tenant-Id"] = bad;
            Assert.False(TenantAuthorization.TryGetTenantId(headers, out _));
        }
        headers.Append("X-Tenant-Id", id.ToString());
        Assert.False(TenantAuthorization.TryGetTenantId(headers, out _));
    }
}
