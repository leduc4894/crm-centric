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

    [Theory]
    [InlineData("string", "\"private-value\"", "\"private-value\"", "42")]
    [InlineData("number", "987654321098765432109876543210.123456789", "987654321098765432109876543210.1234567890", "\"42\"")]
    [InlineData("boolean", "false", "false", "0")]
    [InlineData("date", "\"2024-02-29\"", "\"2024-02-29\"", "\"2023-02-29\"")]
    public async Task Values_are_typed_versioned_and_journaled(string type, string value, string equivalent, string invalid)
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Test"}""");
        var field = await CreateId(Path, Body.Replace("string", type));
        var path = $"/api/v1/contacts/{id}/fields/{field}";
        string Input(string raw) => "{\"value\":" + raw + "}";
        async Task Put(string raw, int before, int after, string role)
        {
            using var response = await Send(HttpMethod.Put, path, Input(raw), role, match: [$"\"v{before}\""]);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal($"\"v{after}\"", response.Headers.ETag!.ToString());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(id, json.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(after, json.RootElement.GetProperty("version").GetInt32());
        }
        await Problem(await Send(HttpMethod.Get, path), HttpStatusCode.NotFound, "NOT_FOUND");
        foreach (var bad in new[] { Input(invalid), "{}", Input("{}"), Input("[]"), "{", "{\"value\":null,\"value\":null}", "{\"value\":null,\"extra\":0}" })
            await Problem(await Send(HttpMethod.Put, path, bad, match: ["\"v1\""]), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        await Put("null", 1, 1, "agent");
        await Put(value, 1, 2, "admin");
        using var read = await Send(HttpMethod.Get, path, subject: "operator");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var stored = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(value, stored.RootElement.GetProperty("value").GetRawText());
        await Put(equivalent, 2, 2, "owner");
        await Problem(await Send(HttpMethod.Put, path, Input(value), match: ["\"v1\""]), HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        await Put("null", 2, 3, "agent");
        await Problem(await Send(HttpMethod.Get, path), HttpStatusCode.NotFound, "NOT_FOUND");
        Assert.Equal(3, await Count("outbox.events", tenant));
        Assert.Equal(4, await Count("audit.entries", tenant));
        if (type == "string")
        {
            await using var c = new Npgsql.NpgsqlConnection(db.AdminConnectionString);
            await c.OpenAsync();
            await using var cmd = new Npgsql.NpgsqlCommand("""
                SELECT EXISTS(SELECT FROM audit.entries a WHERE to_jsonb(a)::text LIKE '%private-value%')
                    OR EXISTS(SELECT FROM outbox.events e WHERE to_jsonb(e)::text LIKE '%private-value%')
                """, c);
            Assert.Equal(false, await cmd.ExecuteScalarAsync());
        }
    }

    [Fact]
    public async Task Numbers_outside_jsonb_limits_round_trip_and_compare_exactly()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Test"}""");
        var number = await CreateId(Path, Body.Replace("string", "number"));
        var numberPath = $"/api/v1/contacts/{id}/fields/{number}";
        foreach (var (raw, equivalent, version) in new[] {
            ("1e131072", "10e131071", 1), ("1e-16384", "0.1e-16383", 2),
            ("-12e99999999999999999999999999999999999999", "-120e99999999999999999999999999999999999998", 3),
            ("-12e-99999999999999999999999999999999999999", "-120e-100000000000000000000000000000000000000", 4),
            ("-0e99999999999999999999999999999999999999", "0", 5) })
        {
            using var put = await Send(HttpMethod.Put, numberPath, "{\"value\":" + raw + "}", match: [$"\"v{version}\""]);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.Equal($"\"v{version + 1}\"", put.Headers.ETag!.ToString());
            using var numberRead = await Send(HttpMethod.Get, numberPath);
            Assert.Equal(HttpStatusCode.OK, numberRead.StatusCode);
            using var numberJson = JsonDocument.Parse(await numberRead.Content.ReadAsStringAsync());
            Assert.Equal(raw, numberJson.RootElement.GetProperty("value").GetRawText());
            using var same = await Send(HttpMethod.Put, numberPath, "{\"value\":" + equivalent + "}", match: [$"\"v{version + 1}\""]);
            Assert.Equal(HttpStatusCode.OK, same.StatusCode);
            Assert.Equal($"\"v{version + 1}\"", same.Headers.ETag!.ToString());
        }
        Assert.Equal(1, await Count("crm.contact_field_values", tenant));
        Assert.Equal(6, await Count("outbox.events", tenant));
        Assert.Equal(7, await Count("audit.entries", tenant));
        var field = await CreateId(Path, Body.Replace("abc", "text"));
        var path = $"/api/v1/contacts/{id}/fields/{field}";
        var text = new string('a', 9000);
        using var response = await Send(HttpMethod.Put, path, JsonSerializer.Serialize(new { value = text }), match: ["\"v6\""]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var read = await Send(HttpMethod.Get, path);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(text, json.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Direct_database_number_write_enforces_version_and_journals()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Direct"}""");
        var field = await CreateId(Path, Body.Replace("string", "number"));
        await using var app = new Npgsql.NpgsqlConnection(db.ApplicationConnectionString);
        await app.OpenAsync();
        await using var tx = await app.BeginTransactionAsync();
        await using (var set = new Npgsql.NpgsqlCommand("SELECT set_config('crm.tenant_id',$1,true)", app, tx))
        { set.Parameters.AddWithValue(tenant.ToString()); await set.ExecuteNonQueryAsync(); }
        async Task<string> Change(long expected, string raw)
        {
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT platform.mutate_contact_attribute($1,$2,$3,$4,'agent',$5,'numeric-test','field',$6)", app, tx);
            foreach (var value in new object[] { tenant, contact, field, Issuer, expected, raw }) cmd.Parameters.AddWithValue(value);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.Equal("conflict", await Change(2, "1e131072"));
        Assert.Equal("changed", await Change(1, "1e131072"));
        Assert.Equal("conflict", await Change(1, "10e131071"));
        Assert.Equal("unchanged", await Change(2, "10e131071"));
        Assert.Equal("unchanged", await Change(2, " \t\r\n10e131071 \n\t"));
        Assert.Equal("invalid", await Change(2, "\v1"));
        Assert.Equal("invalid", await Change(2, "1\u00a0"));
        Assert.Equal("changed", await Change(2, " \t\r\n2e131072 \n\t"));
        Assert.Equal("unchanged", await Change(3, "20e131071"));
        await tx.CommitAsync();
        Assert.Equal(3, await Count("outbox.events", tenant));
        Assert.Equal(4, await Count("audit.entries", tenant));
        using var read = await Send(HttpMethod.Get, $"/api/v1/contacts/{contact}/fields/{field}");
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("2e131072", json.RootElement.GetProperty("value").GetRawText());
        using var current = await Send(HttpMethod.Get, $"/api/v1/contacts/{contact}");
        Assert.Equal("\"v3\"", current.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Padded_database_number_replaces_existing_exact_value()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Direct"}""");
        var field = await CreateId(Path, Body.Replace("string", "number"));
        var path = $"/api/v1/contacts/{contact}/fields/{field}";
        using (var initial = await Send(HttpMethod.Put, path, """{"value":1e131072}""", match: ["\"v1\""]))
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        await using (var app = new Npgsql.NpgsqlConnection(db.ApplicationConnectionString))
        {
            await app.OpenAsync();
            await using var tx = await app.BeginTransactionAsync();
            await using (var set = new Npgsql.NpgsqlCommand("SELECT set_config('crm.tenant_id',$1,true)", app, tx))
            { set.Parameters.AddWithValue(tenant.ToString()); await set.ExecuteNonQueryAsync(); }
            await using var cmd = new Npgsql.NpgsqlCommand("SELECT platform.mutate_contact_attribute($1,$2,$3,$4,'agent',2,'numeric-test','field',$5)", app, tx);
            foreach (var value in new object[] { tenant, contact, field, Issuer, " \t\r\n2 \n\t" }) cmd.Parameters.AddWithValue(value);
            Assert.Equal("changed", await cmd.ExecuteScalarAsync());
            await tx.CommitAsync();
        }
        using var read = await Send(HttpMethod.Get, path);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("2", json.RootElement.GetProperty("value").GetRawText());
        using var current = await Send(HttpMethod.Get, $"/api/v1/contacts/{contact}");
        Assert.Equal("\"v3\"", current.Headers.ETag!.ToString());
        Assert.Equal(3, await Count("outbox.events", tenant));
        Assert.Equal(4, await Count("audit.entries", tenant));
    }

    [Fact]
    public async Task Legacy_jsonb_number_remains_readable_and_equal_after_upgrade()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Legacy"}""");
        var field = await CreateId(Path, Body.Replace("string", "number"));
        await using (var connection = new Npgsql.NpgsqlConnection(db.MigrationConnectionString))
        {
            await connection.OpenAsync();
            await using var tx = await connection.BeginTransactionAsync();
            await using var set = new Npgsql.NpgsqlCommand("SELECT set_config('crm.tenant_id',$1,true)", connection, tx);
            set.Parameters.AddWithValue(tenant.ToString());
            await set.ExecuteNonQueryAsync();
            await using var seed = new Npgsql.NpgsqlCommand("INSERT INTO crm.contact_field_values(tenant_id,contact_id,field_id,data_type,value) VALUES($1,$2,$3,'number','123.4500'::jsonb)", connection, tx);
            seed.Parameters.AddWithValue(tenant); seed.Parameters.AddWithValue(contact); seed.Parameters.AddWithValue(field);
            await seed.ExecuteNonQueryAsync();
            await tx.CommitAsync();
        }
        var path = $"/api/v1/contacts/{contact}/fields/{field}";
        using var read = await Send(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var value = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(123.45m, value.RootElement.GetProperty("value").GetDecimal());
        using var same = await Send(HttpMethod.Put, path, """{"value":12345e-2}""", match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal("\"v1\"", same.Headers.ETag!.ToString());
        Assert.Equal(1, await Count("outbox.events", tenant));
    }

    [Fact]
    public async Task Very_long_exponents_compare_without_numeric_conversion()
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Test"}""");
        var field = await CreateId(Path, Body.Replace("string", "number"));
        var path = $"/api/v1/contacts/{id}/fields/{field}";
        var exponent = new string('9', 20000);
        using var first = await Send(HttpMethod.Put, path, "{\"value\":1e" + exponent + "}", match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var read = await Send(HttpMethod.Get, path);
        using var returned = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("1e" + exponent, returned.RootElement.GetProperty("value").GetRawText());
        using var same = await Send(HttpMethod.Put, path, "{\"value\":10e" + exponent[..^1] + "8}", match: ["\"v2\""]);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        Assert.Equal("\"v2\"", same.Headers.ETag!.ToString());
        var coefficient = new string('9', 150000);
        using var large = await Send(HttpMethod.Put, path, "{\"value\":" + coefficient + "}", match: ["\"v2\""]);
        Assert.Equal(HttpStatusCode.OK, large.StatusCode);
        using var reread = await Send(HttpMethod.Get, path);
        using var returnedLarge = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
        Assert.Equal(coefficient, returnedLarge.RootElement.GetProperty("value").GetRawText());
    }

    [Fact]
    public async Task Strings_with_nul_round_trip_and_compare_by_decoded_value()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Here"}""");
        var field = await CreateId(Path, Body);
        var path = $"/api/v1/contacts/{contact}/fields/{field}";
        foreach (var (input, before) in new[] { ("""{"value":"a\u0000"}""", 1), ("""{"value":"\u0061\u0000"}""", 2) })
        {
            using var put = await Send(HttpMethod.Put, path, input, match: [$"\"v{before}\""]);
            Assert.Equal(HttpStatusCode.OK, put.StatusCode);
            Assert.Equal("\"v2\"", put.Headers.ETag!.ToString());
        }
        using var read = await Send(HttpMethod.Get, path);
        using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("a\0", json.RootElement.GetProperty("value").GetString());
        using var literal = await Send(HttpMethod.Put, path, """{"value":"a\\u0000"}""", match: ["\"v2\""]);
        Assert.Equal("\"v3\"", literal.Headers.ETag!.ToString());
    }

    [Fact]
    public async Task Value_access_enforces_tenant_permissions_preconditions_and_required_null()
    {
        var contact = await CreateId("/api/v1/contacts", """{"displayName":"Here"}""");
        var foreign = await CreateId("/api/v1/contacts", """{"displayName":"There"}""", true);
        var field = await CreateId(Path, Body.Replace("}", ",\"required\":true}"));
        var foreignField = await CreateId(Path, Body, true);
        string Url(Guid id, Guid def) => $"/api/v1/contacts/{id}/fields/{def}";
        var path = Url(contact, field);
        var foreignPath = Url(foreign, foreignField);
        using (var foreignWrite = await Send(HttpMethod.Put, foreignPath, "{\"value\":\"foreign\"}", "other", other.ToString(), match: ["\"v1\""]))
            Assert.Equal(HttpStatusCode.OK, foreignWrite.StatusCode);
        using (var foreignRead = await Send(HttpMethod.Get, foreignPath, subject: "other", selected: other.ToString()))
        {
            Assert.Equal(HttpStatusCode.OK, foreignRead.StatusCode);
            using var content = JsonDocument.Parse(await foreignRead.Content.ReadAsStringAsync());
            Assert.Equal("foreign", content.RootElement.GetProperty("value").GetString());
        }
        await Problem(await Send(HttpMethod.Get, foreignPath), HttpStatusCode.NotFound, "NOT_FOUND");
        const string body = """{"value":null}""";
        await Problem(await Send(HttpMethod.Put, path, body), (HttpStatusCode)428, "PRECONDITION_REQUIRED");
        foreach (var bad in new string[][] { ["*"], ["W/\"v1\""], ["\"v1\"", "\"v1\""] })
            await Problem(await Send(HttpMethod.Put, path, body, match: bad), HttpStatusCode.BadRequest, "PRECONDITION_INVALID");
        await Problem(await Send(HttpMethod.Put, path, body, match: ["\"v1\""]), HttpStatusCode.BadRequest, "REQUEST_INVALID");
        await Problem(await Send(HttpMethod.Put, path, body, "operator"), HttpStatusCode.Forbidden, "INSUFFICIENT_SCOPE");
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            await Problem(await Send(method, path, body, anonymous: true), HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED");
            await Problem(await Send(method, path, body, "revoked"), HttpStatusCode.Forbidden, "TENANT_ACCESS_DENIED");
            foreach (var missing in new[] { Url(foreign, field), Url(contact, foreignField), Url(Guid.NewGuid(), field), Url(contact, Guid.NewGuid()) })
                await Problem(await Send(method, missing, body, match: ["\"v1\""]), HttpStatusCode.NotFound, "NOT_FOUND");
        }
        using var deleted = await Send(HttpMethod.Delete, $"/api/v1/contacts/{contact}", match: ["\"v1\""]);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Problem(await Send(HttpMethod.Put, path, body, match: ["\"v1\""]), HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Journal_failure_rolls_back_value_and_contact(bool audit)
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Test"}""");
        var field = await CreateId(Path, Body);
        await using var c = new Npgsql.NpgsqlConnection(db.MigrationConnectionString);
        await c.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(audit
            ? "ALTER TABLE audit.entries ADD CHECK(action <> 'contact.updated')"
            : "ALTER TABLE outbox.events ADD CHECK(type <> 'crm.contact.updated.v1')", c);
        await cmd.ExecuteNonQueryAsync();
        await Problem(await Send(HttpMethod.Put, $"/api/v1/contacts/{id}/fields/{field}", """{"value":"private"}""", match: ["\"v1\""]), HttpStatusCode.ServiceUnavailable, "CRM_STORE_UNAVAILABLE");
        using var contact = await Send(HttpMethod.Get, $"/api/v1/contacts/{id}");
        Assert.Equal("\"v1\"", contact.Headers.ETag!.ToString());
        Assert.Equal(0, await Count("crm.contact_field_values", tenant));
        Assert.Equal(2, await Count("audit.entries", tenant));
        Assert.Equal(1, await Count("outbox.events", tenant));
    }
}
