using System.Net;
using System.Text.Json;
using Xunit;

namespace Integration;

public sealed class TagEndpointsTests : DefinitionEndpointFixture
{
    private const string Path = "/api/v1/tags";
    private const string Body = "{\"name\":\" Label \"}";
    [Fact]
    public async Task Concurrent_creation_is_unique_and_audited()
        => await VerifyCreation(Path, Body, "{\"name\":\"label\"}", "agent", "tag.created");

    [Fact]
    public async Task Permissions_validation_and_pagination()
    {
        foreach (var bad in new[] { "{}", "{\"name\":\"\",\"name\":\"a\"}", "{\"name\":\"x\",\"tenant_id\":\"x\"}", "{\"name\":\"\"}" })
            await Problem(await Send(HttpMethod.Post, Path, bad), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        foreach (var role in new[] { "operator" })
            await Problem(await Send(HttpMethod.Post, Path, Body, role), HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(HttpMethod.Get, Path, anonymous: true)).StatusCode);
        await Problem(await Send(HttpMethod.Post, Path, Body, "revoked"), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
        await Problem(await Send(HttpMethod.Post, Path, Body, "owner", other.ToString()), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
        foreach (var input in new[] { Body, Body.Replace("Label", "second") })
        {
            using var createResponse = await Send(HttpMethod.Post, Path, input);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
            using var expected = JsonDocument.Parse(input);
            Assert.Equal(expected.RootElement.GetProperty("name").GetString(), created.RootElement.GetProperty("name").GetString());
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
        await Problem(await Send(HttpMethod.Get, "/api/v1/custom-fields?cursor=" + cursor), HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }
    [Fact]
    public async Task Audit_failure_rolls_back_creation() =>
        await VerifyAuditRollback(Path, Body, "crm.tags", "tag.created");
}
