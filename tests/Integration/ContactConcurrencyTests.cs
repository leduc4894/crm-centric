using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration;

public sealed partial class ContactEndpointsTests
{
    private async Task<Guid> CreateContact(string email = "first@example.test")
    {
        using var response=await Send(HttpMethod.Post,"/api/v1/contacts",JsonSerializer.Serialize(new { displayName="Original", email, phone="123" }));
        Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        using var result=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return result.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Patch_delete_validate_preconditions_and_patch_omitted_null_and_unique_email()
    {
        var id=await CreateContact(); var otherId=await CreateContact("other@example.test"); var path=$"/api/v1/contacts/{id}";
        using (var missing=await Send(HttpMethod.Patch,path,"{\"phone\":null}")) await Problem(missing,(HttpStatusCode)428,"PRECONDITION_REQUIRED");
        foreach (var match in new[]{"*","W/\"v1\"","\"v1\",\"v2\"","\"v0\""})
        { using var invalid=await Send(HttpMethod.Patch,path,"{\"phone\":null}",match:match); await Problem(invalid,HttpStatusCode.BadRequest,"PRECONDITION_INVALID"); }
        using (var stale=await Send(HttpMethod.Patch,path,"{\"phone\":null}",match:"\"v2\"")) await Problem(stale,HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
        foreach (var body in new[]{"{}","[]","{\"displayName\":null}","{\"displayName\":\"\"}","{\"phone\":null,\"phone\":\"1\"}","{\"tenantId\":\"foreign\"}","{\"email\":\"bad\"}"})
        { using var invalid=await Send(HttpMethod.Patch,path,body,match:"\"v1\""); await Problem(invalid,HttpStatusCode.BadRequest,"REQUEST_INVALID"); }
        using (var conflict=await Send(HttpMethod.Patch,path,"{\"email\":\"OTHER@example.test\"}",match:"\"v1\"")) await Problem(conflict,HttpStatusCode.Conflict,"CONTACT_EMAIL_CONFLICT");
        Assert.Equal(2,await Count("outbox.events",tenant));
        using (var changed=await Send(HttpMethod.Patch,path,"{\"phone\":null}",match:"\"v1\""))
        { Assert.Equal(HttpStatusCode.OK,changed.StatusCode); Assert.Equal("\"v2\"",changed.Headers.ETag!.ToString()); using var body=JsonDocument.Parse(await changed.Content.ReadAsStringAsync()); Assert.Equal("Original",body.RootElement.GetProperty("displayName").GetString()); Assert.Equal("first@example.test",body.RootElement.GetProperty("email").GetString()); Assert.Equal(JsonValueKind.Null,body.RootElement.GetProperty("phone").ValueKind); }
        using (var noOp=await Send(HttpMethod.Patch,path,"{\"phone\":null}",match:"\"v2\"")) { Assert.Equal(HttpStatusCode.OK,noOp.StatusCode); Assert.Equal("\"v2\"",noOp.Headers.ETag!.ToString()); }
        using (var missing=await Send(HttpMethod.Delete,path)) await Problem(missing,(HttpStatusCode)428,"PRECONDITION_REQUIRED");
        using (var deleted=await Send(HttpMethod.Delete,path,match:"\"v2\"")) Assert.Equal(HttpStatusCode.NoContent,deleted.StatusCode);
        using (var read=await Send(HttpMethod.Get,path)) await Problem(read,HttpStatusCode.NotFound,"NOT_FOUND");
        using (var stale=await Send(HttpMethod.Delete,path,match:"\"v2\"")) await Problem(stale,HttpStatusCode.PreconditionFailed,"VERSION_CONFLICT");
        using (var cross=await Send(HttpMethod.Patch,$"/api/v1/contacts/{otherId}","{\"phone\":null}",subject:"other",selected:other.ToString(),match:"\"v1\"")) await Problem(cross,HttpStatusCode.NotFound,"NOT_FOUND");
        Assert.Equal(4,await Count("outbox.events",tenant));
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
        await using (var set=new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx)) { set.Parameters.AddWithValue("t",tenant.ToString()); await set.ExecuteNonQueryAsync(); }
        await using var cmd=new NpgsqlCommand("SELECT type,payload->>'eventType',(payload->'payload'->>'version')::bigint,payload::text FROM outbox.events WHERE contact_id=@id AND type IN ('crm.contact.updated.v1','crm.contact.deleted.v1') ORDER BY (payload->'payload'->>'version')::bigint",c,tx); cmd.Parameters.AddWithValue("id",id);
        await using var reader=await cmd.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); Assert.Equal("crm.contact.updated.v1",reader.GetString(0)); Assert.Equal(reader.GetString(0),reader.GetString(1)); Assert.Equal(2,reader.GetInt64(2)); AssertPersistedIntent(reader.GetString(3),id,"crm.contact.updated.v1",2); Assert.True(await reader.ReadAsync()); Assert.Equal("crm.contact.deleted.v1",reader.GetString(0)); Assert.Equal(reader.GetString(0),reader.GetString(1)); Assert.Equal(3,reader.GetInt64(2)); AssertPersistedIntent(reader.GetString(3),id,"crm.contact.deleted.v1",3); Assert.False(await reader.ReadAsync());
    }

    private void AssertPersistedIntent(string json,Guid id,string type,long version)
    {
        using var doc=JsonDocument.Parse(json); var root=doc.RootElement;
        Assert.Equal(new[]{"contactId","eventId","eventType","occurredAt","payload","tenantId"},root.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.True(Guid.TryParse(root.GetProperty("eventId").GetString(),out _)); Assert.Equal(tenant,root.GetProperty("tenantId").GetGuid()); Assert.Equal(id,root.GetProperty("contactId").GetGuid());
        Assert.Equal(type,root.GetProperty("eventType").GetString()); Assert.True(DateTimeOffset.TryParse(root.GetProperty("occurredAt").GetString(),out var occurred)); Assert.Equal(TimeSpan.Zero,occurred.Offset);
        var payload=root.GetProperty("payload"); Assert.Equal(new[]{"version"},payload.EnumerateObject().Select(x=>x.Name).ToArray()); Assert.Equal(version,payload.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Patch_clears_email_and_denies_cross_tenant_and_read_only_mutations()
    {
        var id=await CreateContact(); var path=$"/api/v1/contacts/{id}";
        using(var wrong=await Send(HttpMethod.Delete,path,subject:"other",selected:other.ToString(),match:"\"v1\"")) await Problem(wrong,HttpStatusCode.NotFound,"NOT_FOUND");
        using(var denied=await Send(HttpMethod.Patch,path,"{\"email\":null}",subject:"operator",match:"\"v1\"")) await Problem(denied,HttpStatusCode.Forbidden,"INSUFFICIENT_SCOPE");
        using(var cleared=await Send(HttpMethod.Patch,path,"{\"email\":null}",match:"\"v1\""))
        { Assert.Equal(HttpStatusCode.OK,cleared.StatusCode); Assert.Equal("\"v2\"",cleared.Headers.ETag!.ToString()); using var data=JsonDocument.Parse(await cleared.Content.ReadAsStringAsync()); Assert.Equal(JsonValueKind.Null,data.RootElement.GetProperty("email").ValueKind); Assert.Equal("123",data.RootElement.GetProperty("phone").GetString()); }
        using(var created=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"New\",\"email\":\"first@example.test\"}")) Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        Assert.Equal(3,await Count("outbox.events",tenant));
    }

    [Theory]
    [InlineData("patch","patch")]
    [InlineData("patch","delete")]
    [InlineData("delete","delete")]
    [InlineData("patch","upsert")]
    [InlineData("delete","upsert")]
    public async Task Concurrent_mutations_of_same_version_have_one_winner(string first,string second)
    {
        var id=await CreateContact(); var path=$"/api/v1/contacts/{id}";
        Task<HttpResponseMessage> Mutate(string kind,string name) => kind switch {
            "patch" => Send(HttpMethod.Patch,path,JsonSerializer.Serialize(new {displayName=name}),match:"\"v1\""),
            "delete" => Send(HttpMethod.Delete,path,match:"\"v1\""),
            _ => Send(HttpMethod.Post,"/api/v1/contacts",JsonSerializer.Serialize(new {displayName=name,email="first@example.test"}),match:"\"v1\"",contactId:id.ToString()) };
        var results=await Task.WhenAll(Mutate(first,"A"),Mutate(second,"B"));
        try { Assert.Single(results,x=>x.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent); Assert.Single(results,x=>x.StatusCode==HttpStatusCode.PreconditionFailed); }
        finally { foreach(var response in results) response.Dispose(); }
        Assert.Equal(2,await Count("outbox.events",tenant));
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=@tenant AND target_id=@id AND action IN ('contact.created','contact.updated','contact.deleted')",c); cmd.Parameters.AddWithValue("tenant",tenant); cmd.Parameters.AddWithValue("id",id); Assert.Equal(2L,await cmd.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Post_and_patch_competing_for_same_email_follow_winner_and_leave_one_journal(bool patchWins)
    {
        var id=await CreateContact("original@example.test");
        await using var admin=new NpgsqlConnection(db.MigrationConnectionString); await admin.OpenAsync();
        var trigger=patchWins
            ? $"CREATE FUNCTION crm.race_gate() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.tenant_id='{tenant}' AND NEW.email='race@example.test' THEN PERFORM pg_advisory_xact_lock(732062); END IF; RETURN NEW; END $$; CREATE TRIGGER race_gate BEFORE INSERT ON crm.contacts FOR EACH ROW EXECUTE FUNCTION crm.race_gate()"
            : $"CREATE FUNCTION outbox.race_gate() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.tenant_id='{tenant}' AND NEW.contact_id<>'{id}' AND NEW.type='crm.contact.created.v1' THEN PERFORM pg_advisory_xact_lock(732062); END IF; RETURN NEW; END $$; CREATE TRIGGER race_gate BEFORE INSERT ON outbox.events FOR EACH ROW EXECUTE FUNCTION outbox.race_gate()";
        await using (var setup=new NpgsqlCommand(trigger+"; SELECT pg_advisory_lock(732062)",admin)) await setup.ExecuteNonQueryAsync();
        var post=Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"New\",\"email\":\"race@example.test\"}");
        Task<HttpResponseMessage>? patch=null;
        try
        {
            var postPid=await FindBlockedPid(admin.ProcessID);
            using(var otherTenant=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Other\",\"email\":\"race@example.test\"}","other",other.ToString()).WaitAsync(TimeSpan.FromSeconds(8))) Assert.Equal(HttpStatusCode.Created,otherTenant.StatusCode);
            patch=Send(HttpMethod.Patch,$"/api/v1/contacts/{id}","{\"email\":\"RACE@example.test\"}",match:"\"v1\"");
            if (patchWins)
            {
                using var winner=await patch.WaitAsync(TimeSpan.FromSeconds(8)); Assert.Equal(HttpStatusCode.OK,winner.StatusCode);
            }
            else await FindBlockedPid(postPid);
            await using (var release=new NpgsqlCommand("SELECT pg_advisory_unlock(732062)",admin)) await release.ExecuteNonQueryAsync();
            using var postResult=await post.WaitAsync(TimeSpan.FromSeconds(8));
            if (patchWins) await Problem(postResult,(HttpStatusCode)428,"PRECONDITION_REQUIRED");
            else
            {
                Assert.Equal(HttpStatusCode.Created,postResult.StatusCode);
                using var loser=await patch.WaitAsync(TimeSpan.FromSeconds(8)); await Problem(loser,HttpStatusCode.Conflict,"CONTACT_EMAIL_CONFLICT");
            }
            Assert.Equal(patchWins ? 1 : 2,await Count("crm.contacts",tenant));
            Assert.Equal(2,await Count("outbox.events",tenant));
            await using var count=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=@t AND action IN ('contact.created','contact.updated')",admin); count.Parameters.AddWithValue("t",tenant); Assert.Equal(2L,await count.ExecuteScalarAsync());
        }
        finally
        {
            await using var unlock=new NpgsqlCommand("SELECT pg_advisory_unlock_all()",admin); await unlock.ExecuteNonQueryAsync();
            try { (await post.WaitAsync(TimeSpan.FromSeconds(8))).Dispose(); } catch (TimeoutException) { }
            if (patch is not null) try { (await patch.WaitAsync(TimeSpan.FromSeconds(8))).Dispose(); } catch (TimeoutException) { }
        }
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_patch_and_delete()
    {
        var id=await CreateContact(); var path=$"/api/v1/contacts/{id}";
        await using (var c=new NpgsqlConnection(db.MigrationConnectionString)) { await c.OpenAsync(); await using var cmd=new NpgsqlCommand("CREATE FUNCTION outbox.fail_contact_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'failure'; END $$; CREATE TRIGGER fail_contact_test BEFORE INSERT ON outbox.events FOR EACH ROW EXECUTE FUNCTION outbox.fail_contact_test()",c); await cmd.ExecuteNonQueryAsync(); }
        using (var patch=await Send(HttpMethod.Patch,path,"{\"displayName\":\"Changed\"}",match:"\"v1\"")) await Problem(patch,HttpStatusCode.ServiceUnavailable,"CRM_STORE_UNAVAILABLE");
        using (var delete=await Send(HttpMethod.Delete,path,match:"\"v1\"")) await Problem(delete,HttpStatusCode.ServiceUnavailable,"CRM_STORE_UNAVAILABLE");
        using (var read=await Send(HttpMethod.Get,path)) { Assert.Equal(HttpStatusCode.OK,read.StatusCode); Assert.Equal("\"v1\"",read.Headers.ETag!.ToString()); using var data=JsonDocument.Parse(await read.Content.ReadAsStringAsync()); Assert.Equal("Original",data.RootElement.GetProperty("displayName").GetString()); }
        Assert.Equal(1,await Count("outbox.events",tenant));
        await using var check=new NpgsqlConnection(db.MigrationConnectionString); await check.OpenAsync(); await using var audit=new NpgsqlCommand("SELECT count(*) FROM audit.entries WHERE tenant_id=@t AND target_id=@id",check); audit.Parameters.AddWithValue("t",tenant); audit.Parameters.AddWithValue("id",id); Assert.Equal(1L,await audit.ExecuteScalarAsync());
    }
}
