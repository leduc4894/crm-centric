using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class TimelineEndpointsTests : DefinitionEndpointFixture
{
    private async Task<JsonElement> Page(string url, string subject = "owner", string? selected = null)
    {
        using var response = await Send(HttpMethod.Get, url, subject: subject, selected: selected);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Escaped_supplementary_characters_fit_the_scalar_limit()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        var text = string.Concat(Enumerable.Repeat("😀", 1000));
        var json = JsonSerializer.Serialize(new { text });
        Assert.True(json.Length > 8192);
        using var created = await Send(HttpMethod.Post, $"/api/v1/contacts/{id}/notes", json, match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var result = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.Equal(text, result.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Direct_function_requires_a_scoped_active_writer_and_matching_version()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        await using var app = new NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using var scope = new NpgsqlCommand("SELECT set_config('crm.tenant_id',$1,false)", app);
        scope.Parameters.AddWithValue(tenant.ToString());
        await scope.ExecuteScalarAsync();
        async Task<object?> Write(Guid selected, string actor, long expected)
        {
            await using var cmd = new NpgsqlCommand("SELECT new_version FROM platform.create_contact_note($1,$2,$3,$4,$5,'test',ARRAY['text'])", app);
            foreach (var value in new object[] { selected, id, Issuer, actor, expected }) cmd.Parameters.Add(new NpgsqlParameter { Value = value });
            return await cmd.ExecuteScalarAsync();
        }
        foreach (var (selected, actor) in new[] { (tenant, "operator"), (tenant, "revoked"), (other, "other") })
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, (await Assert.ThrowsAsync<PostgresException>(() => Write(selected, actor, 1))).SqlState);
        Assert.Null(await Write(tenant, "agent", 2));
        Assert.Equal(2L, await Write(tenant, "agent", 1));
        Assert.Null(await Write(tenant, "agent", 1));
        Assert.Equal(1, await Count("crm.timeline_entries", tenant));
        Assert.Equal(2, await Count("audit.entries", tenant));
        Assert.Equal(2, await Count("outbox.events", tenant));
    }

    [Fact]
    public async Task Scope_access_and_soft_deletion_apply_to_both_routes()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Primary"}""");
        var path = $"/api/v1/contacts/{id}";
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            var url = path + (method == HttpMethod.Get ? "/timeline" : "/notes");
            var body = method == HttpMethod.Post ? """{"text":"new"}""" : null;
            await Problem(await Send(method, url, body, anonymous: true), HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
            await Problem(await Send(method, url, body, "revoked"), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
            await Problem(await Send(method, url, body, selected: "invalid"), HttpStatusCode.BadRequest, "TENANT_CONTEXT_INVALID");
            await Problem(await Send(method, url, body, "other", other.ToString(), match: ["\"v1\""]), HttpStatusCode.NotFound, "NOT_FOUND");
        }
        await Problem(await Send(HttpMethod.Post, path + "/notes", """{"text":"new"}""", "operator", match: ["\"v1\""]), HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        using var deleted = await Send(HttpMethod.Delete, path, match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Problem(await Send(HttpMethod.Get, path + "/timeline"), HttpStatusCode.NotFound, "NOT_FOUND");
        await Problem(await Send(HttpMethod.Post, path + "/notes", """{"text":"new"}""", match: ["\"v2\""]), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Keyset_pages_use_tenant_contact_time_and_uuid_even_when_anchor_is_removed()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Primary"}""");
        var second = await CreateId("/api/v1/contacts", """{"displayName":"Second"}""");
        string Url(Guid contact) => $"/api/v1/contacts/{contact}/timeline";
        var url = Url(id);
        Assert.Empty((await Page(url)).GetProperty("items").EnumerateArray());
        await using var conn = new NpgsqlConnection(db.AdminConnectionString);
        await conn.OpenAsync();
        async Task Sql(string sql, params object[] args)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            foreach (var value in args) cmd.Parameters.Add(new NpgsqlParameter { Value = value });
            await cmd.ExecuteNonQueryAsync();
        }
        const string seed = """
            INSERT INTO crm.timeline_entries(tenant_id,id,contact_id,entry_kind,source,type,occurred_at,summary)
            SELECT $1, ('00000000-0000-0000-0000-'||lpad(i::text,12,'0'))::uuid,$2,'note','crm','crm.note.created.v1','2026-01-01Z',convert_to(i::text,'UTF8')
            FROM generate_series($3,$4) AS i
            """;
        await Sql(seed, tenant, id, 1, 1);
        Assert.Single((await Page(url)).GetProperty("items").EnumerateArray());
        await Sql(seed, tenant, id, 2, 20);
        var twenty = await Page(url);
        Assert.Equal(20, twenty.GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, twenty.GetProperty("nextCursor").ValueKind);
        await Sql(seed, tenant, id, 21, 21);
        var first = await Page(url);
        var token = first.GetProperty("nextCursor").GetString()!;
        var entries = first.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(2, 20).Reverse(), entries.Select(x => int.Parse(x.GetProperty("summary").GetString()!)));
        await Sql("INSERT INTO crm.contacts(tenant_id,id,display_name) VALUES ($1,$2,'Foreign')", other, id);
        Assert.Empty((await Page(url, "other", other.ToString())).GetProperty("items").EnumerateArray());
        await Problem(await Send(HttpMethod.Get, url + "?cursor=" + token, subject: "other", selected: other.ToString()), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Problem(await Send(HttpMethod.Get, Url(second) + "?cursor=" + token), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Problem(await Send(HttpMethod.Get, url + "?cursor=" + (token[0] == 'A' ? "B" : "A") + token[1..]), HttpStatusCode.BadRequest, "CURSOR_INVALID");
        await Sql("DELETE FROM crm.timeline_entries WHERE tenant_id=$1 AND id=$2", tenant, entries[^1].GetProperty("id").GetGuid());
        await Sql(seed, tenant, id, 0, 0);
        var next = await Page(url + "?cursor=" + token);
        Assert.Equal(new[] { "1", "0" }, next.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("summary").GetString()));
        foreach (var bad in new[] { "0", "101", "x", "1&limit=2" })
            await Problem(await Send(HttpMethod.Get, url + "?limit=" + bad), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        Assert.Single((await Page(url + "?limit=1")).GetProperty("items").EnumerateArray());
        Assert.Equal(21, (await Page(url + "?limit=100")).GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData("crm.timeline_entries")]
    [InlineData("audit.entries")]
    [InlineData("outbox.events")]
    [InlineData("commit")]
    public async Task Failures_rollback_entry_version_and_journal(string failure)
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        await using var c = new NpgsqlConnection(db.AdminConnectionString);
        await c.OpenAsync();
        var table = failure == "commit" ? "crm.timeline_entries" : failure;
        var trigger = failure == "commit" ? "CONSTRAINT TRIGGER fault AFTER INSERT" : "TRIGGER fault BEFORE INSERT";
        var timing = failure == "commit" ? "DEFERRABLE INITIALLY DEFERRED" : "";
        await using var cmd = new NpgsqlCommand($"""
            CREATE FUNCTION crm.fail_insert() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'fixture'; END $$;
            CREATE {trigger} ON {table} {timing} FOR EACH ROW EXECUTE FUNCTION crm.fail_insert();
            """, c);
        await cmd.ExecuteNonQueryAsync();
        var path = $"/api/v1/contacts/{id}";
        await Problem(await Send(HttpMethod.Post, path + "/notes", """{"text":"new"}""", match: ["\"v1\""]), HttpStatusCode.ServiceUnavailable, "CRM_STORE_UNAVAILABLE");
        Assert.Equal(0, await Count("crm.timeline_entries", tenant));
        Assert.Equal(1, await Count("audit.entries", tenant));
        Assert.Equal(1, await Count("outbox.events", tenant));
        using var contact = await Send(HttpMethod.Get, path);
        Assert.Equal("\"v1\"", contact.Headers.ETag?.ToString());
    }

    [Fact]
    public async Task Notes_round_trip_contract_text_and_advance_strong_contact_etag()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        var path = $"/api/v1/contacts/{id}";
        var text = "<b>😀\0é</b>" + new string('x', 990);
        var version = 1;
        foreach (var actor in new[] { "owner", "admin", "agent" })
        {
            using var created = await Send(HttpMethod.Post, path + "/notes", JsonSerializer.Serialize(new { text }), actor, match: [$"\"v{version}\""]);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal($"\"v{++version}\"", created.Headers.ETag?.ToString());
            using var entry = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.Equal(text, entry.RootElement.GetProperty("summary").GetString());
            Assert.Equal("crm.note.created.v1", entry.RootElement.GetProperty("type").GetString());
            Assert.Equal("crm", entry.RootElement.GetProperty("source").GetString());
            Assert.Equal(JsonValueKind.Null, entry.RootElement.GetProperty("inboxId").ValueKind);
            Assert.Equal(id, entry.RootElement.GetProperty("contactId").GetGuid());
        }
        foreach (var actor in new[] { "owner", "admin", "agent", "operator" })
        {
            using var list = await Send(HttpMethod.Get, path + "/timeline", subject: actor);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var page = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal(3, page.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(text, page.RootElement.GetProperty("items")[0].GetProperty("summary").GetString());
        }
        await using var c = new NpgsqlConnection(db.AdminConnectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM outbox.events WHERE contact_id=$1 AND type='crm.contact.updated.v1' AND payload->'payload'=jsonb_build_object('version',(payload->'payload'->'version'))", c);
        cmd.Parameters.AddWithValue(id);
        Assert.Equal(3L, await cmd.ExecuteScalarAsync());
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=$1 AND target_id=$2 AND action='contact.updated' AND summary='Contact changed'", c);
        audit.Parameters.AddWithValue(tenant);
        audit.Parameters.AddWithValue(id);
        Assert.Equal(3L, await audit.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Invalid_inputs_and_preconditions_leave_contact_unchanged()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        var path = $"/api/v1/contacts/{id}/notes";
        foreach (var body in new[] { "null", "{", "{}", """{"text":""}""", """{"text":"a","text":"b"}""", """{"text":"a","extra":1}""", JsonSerializer.Serialize(new { text = new string('x', 1001) }) })
            await Problem(await Send(HttpMethod.Post, path, body, match: ["\"v1\""]), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        foreach (var (tag, status, reason) in new[] { ((string?)null, 428, "PRECONDITION_REQUIRED"), ("*", 400, "PRECONDITION_INVALID"), ("\"v2\"", 412, "VERSION_CONFLICT") })
            await Problem(await Send(HttpMethod.Post, path, """{"text":"ok"}""", match: tag is null ? null : [tag]), (HttpStatusCode)status, reason);
        Assert.Equal(0, await Count("crm.timeline_entries", tenant));
        Assert.Equal(1, await Count("outbox.events", tenant));
    }
}
