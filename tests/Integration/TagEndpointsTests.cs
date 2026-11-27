using System.Net;
using System.Text.Json;
using Xunit;

namespace Integration;

public sealed class TagEndpointsTests : DefinitionEndpointFixture
{
    private const string Path = "/api/v1/tags";
    private const string Body = "{\"name\":\" Label \"}";
    [Fact]
    public async Task Application_database_mutation_enforces_version_and_commits_both_journals()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Contact"}""");
        var tag = await CreateId(Path, Body);
        await using var app = new Npgsql.NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using var set = new Npgsql.NpgsqlCommand("SELECT set_config('crm.tenant_id',$1,true)", app, tx);
        set.Parameters.Add(new() { Value = tenant.ToString() });
        await set.ExecuteNonQueryAsync();
        async Task<string> Change(long expected)
        {
            await using var cmd = new Npgsql.NpgsqlCommand("""
                SELECT platform.mutate_contact_attribute($1,$2,$3,$4,'agent',$5,'attribute-test','attach',NULL)
                """, app, tx);
            foreach (var arg in new object[] { tenant, contact, tag, Issuer, expected })
                cmd.Parameters.Add(new() { Value = arg });
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.Equal("conflict", await Change(2));
        Assert.Equal("changed", await Change(1));
        Assert.Equal("conflict", await Change(1));
        Assert.Equal("unchanged", await Change(2));
        await tx.CommitAsync();
        Assert.Equal(1, await Count("crm.contact_tags", tenant));
        Assert.Equal(2, await Count("outbox.events", tenant));
        Assert.Equal(3, await Count("audit.entries", tenant));
        using var result = await Send(HttpMethod.Get, $"/api/v1/contacts/{contact}");
        Assert.Equal("\"v2\"", result.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Definition_names_round_trip_embedded_nul_and_preserve_tag_uniqueness()
    {
        var label = " X\0Y ";
        await CreateId(Path, JsonSerializer.Serialize(new { name = label }));
        await Problem(await Send(HttpMethod.Post, Path, JsonSerializer.Serialize(new { name = "x\0y" })),
            HttpStatusCode.Conflict, "TAG_NAME_CONFLICT");
        await CreateId(Path, JsonSerializer.Serialize(new { name = "x\u0001y" }));
        const string fields = "/api/v1/custom-fields";
        await CreateId(fields, JsonSerializer.Serialize(new { key = "a", displayName = label, dataType = "string" }));
        foreach (var (url, property) in new[] { (Path,"name"), (fields,"displayName") })
        {
            using var list = JsonDocument.Parse(await (await Send(HttpMethod.Get, url)).Content.ReadAsStringAsync());
            Assert.Contains(list.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty(property).GetString() == label);
        }
    }
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
        await Problem(await Send(HttpMethod.Get, Path, anonymous: true), HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
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

    [Fact]
    public async Task Assignments_are_versioned_filtered_paginated_and_tenant_scoped()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"First"}""");
        var another = await CreateId("/api/v1/contacts", """{"displayName":"Second"}""");
        var foreign = await CreateId("/api/v1/contacts", """{"displayName":"Foreign"}""", true);
        var a = await CreateId(Path, Body);
        var b = await CreateId(Path, """{"name":"B"}""");
        var otherTag = await CreateId(Path, Body, true);
        var tags = $"/api/v1/contacts/{id}/tags";
        var link = $"{tags}/{a}";
        await Problem(await Send(HttpMethod.Put, link), (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        foreach (var invalid in new string[][] { ["*"], ["W/\"v1\""], ["\"v1\"", "\"v1\""] })
            await Problem(await Send(HttpMethod.Put, link, match: invalid), HttpStatusCode.BadRequest, "PRECONDITION_INVALID");
        await Problem(await Send(HttpMethod.Put, link, subject: "operator", match: ["\"v1\""]), HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        foreach (var absent in new[] { Guid.NewGuid(), otherTag })
            await Problem(await Send(HttpMethod.Delete, $"{tags}/{absent}", match: ["\"v1\""]), HttpStatusCode.NotFound, "NOT_FOUND");
        await Problem(await Send(HttpMethod.Get, $"/api/v1/contacts/{foreign}/tags"), HttpStatusCode.NotFound, "NOT_FOUND");
        await Problem(await Send(HttpMethod.Put, $"/api/v1/contacts/{foreign}/tags/{a}", match: ["\"v1\""]), HttpStatusCode.NotFound, "NOT_FOUND");

        async Task Change(HttpMethod method, Guid tag, int before, int after, string role)
        {
            using var result = await Send(method, $"{tags}/{tag}", subject: role, match: [$"\"v{before}\""]);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal($"\"v{after}\"", result.Headers.ETag!.ToString());
            using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(after, doc.RootElement.GetProperty("version").GetInt32());
        }
        async Task<JsonElement> Page(string url)
        {
            using var result = await Send(HttpMethod.Get, url, subject: "operator");
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            using var doc = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
            return doc.RootElement.Clone();
        }
        await Change(HttpMethod.Put, a, 1, 2, "agent");
        await Change(HttpMethod.Put, a, 2, 2, "admin");
        await Problem(await Send(HttpMethod.Put, link, match: ["\"v1\""]), HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Equal(id, Assert.Single((await Page($"/api/v1/contacts?tagId={a}")).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        await Change(HttpMethod.Put, b, 2, 3, "owner");
        var first = await Page(tags + "?limit=1");
        var cursor = first.GetProperty("nextCursor").GetString();
        var last = await Page(tags + "?cursor=" + cursor);
        var ids = new[] { first, last }.Select(page => Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(new[] { a, b }.Order().ToArray(), ids);
        await Problem(await Send(HttpMethod.Get, $"/api/v1/contacts/{another}/tags?cursor={cursor}"), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Problem(await Send(HttpMethod.Get, $"/api/v1/contacts/{foreign}/tags?cursor={cursor}", subject: "other", selected: other.ToString()), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Change(HttpMethod.Delete, a, 3, 4, "admin");
        await Change(HttpMethod.Delete, a, 4, 4, "agent");
        await Problem(await Send(HttpMethod.Delete, link, match: ["\"v3\""]), HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Empty((await Page($"/api/v1/contacts?tagId={a}")).GetProperty("items").EnumerateArray());
        await using var c = new Npgsql.NpgsqlConnection(db.AdminConnectionString);
        await c.OpenAsync();
        await using var journal = new Npgsql.NpgsqlCommand("""
            SELECT (SELECT count(*) FROM audit.entries WHERE tenant_id=$1 AND target_id=$2 AND action='contact.updated'),
                   (SELECT count(*) FROM outbox.events WHERE tenant_id=$1 AND contact_id=$2 AND type='crm.contact.updated.v1')
            """, c);
        journal.Parameters.Add(new() { Value = tenant });
        journal.Parameters.Add(new() { Value = id });
        await using (var r = await journal.ExecuteReaderAsync())
        { Assert.True(await r.ReadAsync()); Assert.Equal(3, r.GetInt64(0)); Assert.Equal(3, r.GetInt64(1)); }
        using var deleted = await Send(HttpMethod.Delete, $"/api/v1/contacts/{id}", match: ["\"v4\""]);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Problem(await Send(HttpMethod.Get, tags), HttpStatusCode.NotFound, "NOT_FOUND");
        await Problem(await Send(HttpMethod.Put, link, match: ["\"v5\""]), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Tag_mutations_after_deletion_return_the_same_problem_as_missing_contacts()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Removed"}""");
        var tag = await CreateId(Path, Body);
        using var deleted = await Send(HttpMethod.Delete, $"/api/v1/contacts/{id}", match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        foreach (var contact in new[] { id, Guid.NewGuid() })
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete })
        foreach (var match in new string[]?[] { ["\"v1\""], null, ["*"] })
        {
            using var response = await Send(method, $"/api/v1/contacts/{contact}/tags/{tag}", match: match);
            await Problem(response, HttpStatusCode.NotFound, "NOT_FOUND");
        }
    }

    [Fact]
    public async Task Failed_outbox_append_rolls_back_tag_version_and_audit()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"First"}""");
        var tag = await CreateId(Path, Body);
        await using var c = new Npgsql.NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("ALTER TABLE outbox.events ADD CHECK(type <> 'crm.contact.updated.v1')", c);
        await cmd.ExecuteNonQueryAsync();
        await Problem(await Send(HttpMethod.Put, $"/api/v1/contacts/{id}/tags/{tag}", match: ["\"v1\""]), HttpStatusCode.ServiceUnavailable, "CRM_STORE_UNAVAILABLE");
        using var contact = await Send(HttpMethod.Get, $"/api/v1/contacts/{id}");
        Assert.Equal("\"v1\"", contact.Headers.ETag!.ToString());
        Assert.Equal(0, await Count("crm.contact_tags", tenant));
        Assert.Equal(2, await Count("audit.entries", tenant));
        Assert.Equal(1, await Count("outbox.events", tenant));
    }
}
