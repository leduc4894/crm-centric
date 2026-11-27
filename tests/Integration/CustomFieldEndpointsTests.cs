using System.Net;
using System.Text.Json;
using Xunit;

namespace Integration;

public sealed class CustomFieldEndpointsTests : DefinitionEndpointFixture
{
    private const string Path = "/api/v1/custom-fields";
    private const string Body = "{\"key\":\"abc\",\"displayName\":\"Label\",\"dataType\":\"string\"}";
    [Fact]
    public async Task Concurrent_creation_is_unique_and_audited()
        => await VerifyCreation(Path, Body, "{\"key\":\"abc\",\"displayName\":\"Label\",\"dataType\":\"string\"}", "admin", "custom_field.created");

    [Fact]
    public async Task Permissions_validation_and_pagination()
    {
        foreach (var bad in new[] { "{}", "{\"key\":\"Abc\",\"displayName\":\"Label\",\"dataType\":\"string\"}", "{\"key\":\"abc\",\"displayName\":\"Label\",\"dataType\":\"array\"}", "{\"key\":\"other\",\"key\":\"abc\",\"displayName\":\"Label\",\"dataType\":\"string\"}", "{\"extra\":true,\"key\":\"abc\",\"displayName\":\"Label\",\"dataType\":\"string\"}" })
            await Problem(await Send(HttpMethod.Post, Path, bad), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        foreach (var role in new[] { "agent", "operator" })
            await Problem(await Send(HttpMethod.Post, Path, Body, role), HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        await Problem(await Send(HttpMethod.Get, Path, anonymous: true), HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
        await Problem(await Send(HttpMethod.Post, Path, Body, "revoked"), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
        await Problem(await Send(HttpMethod.Post, Path, Body, "owner", other.ToString()), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
        foreach (var input in new[] { Body, Body.Replace("abc", "second") })
        {
            using var createResponse = await Send(HttpMethod.Post, Path, input);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            using var expected = JsonDocument.Parse(input);
            Assert.False(created.RootElement.GetProperty("required").GetBoolean());
            Assert.Equal(1, created.RootElement.GetProperty("version").GetInt32());
            foreach (var key in new[] {"key","displayName","dataType"})
                Assert.Equal(expected.RootElement.GetProperty(key).GetString(), created.RootElement.GetProperty(key).GetString());
        }
        var response = await Send(HttpMethod.Get, Path + "?limit=1", subject: "operator");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var first = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var cursor = first.RootElement.GetProperty("nextCursor").GetString();
        using var last = JsonDocument.Parse(await (await Send(HttpMethod.Get, Path + "?cursor=" + cursor)).Content.ReadAsStringAsync());
        var a = first.RootElement.GetProperty("items")[0].GetProperty("id").GetString();
        var b = Assert.Single(last.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetString();
        Assert.True(string.CompareOrdinal(a, b) < 0);
        using var empty = JsonDocument.Parse(await (await Send(HttpMethod.Get, Path, subject: "other", selected: other.ToString())).Content.ReadAsStringAsync());
        Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
        await Problem(await Send(HttpMethod.Get, Path + "?cursor=" + cursor, subject: "other", selected: other.ToString()), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Problem(await Send(HttpMethod.Get, "/api/v1/tags?cursor=" + cursor), HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }
    [Fact]
    public async Task Audit_failure_rolls_back_creation() =>
        await VerifyAuditRollback(Path, Body, "crm.custom_field_defs", "custom_field.created");
}
