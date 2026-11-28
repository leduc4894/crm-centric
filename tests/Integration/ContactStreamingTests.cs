using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Xunit;

namespace Integration;

public sealed partial class ContactEndpointsTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("NOTE")]
    public async Task Slow_body_holds_no_mutation_or_membership_lock_and_rechecks_revocation(string method)
    {
        var id=method != "POST" ? await CreateContact("target@example.test") : Guid.Empty;
        var body=method == "NOTE" ? """{"text":"After"}""" : method == "PATCH" ? "{\"displayName\":\"After\"}" : "{\"displayName\":\"After\",\"email\":\"stream@example.test\"}";
        using var stream=new SlowBodyStream(body);
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var pending=factory.Server.SendAsync(context => {
            context.Request.Method=method == "NOTE" ? "POST" : method;
            context.Request.Path=method == "NOTE" ? $"/api/v1/contacts/{id}/notes" : method == "PATCH" ? $"/api/v1/contacts/{id}" : "/api/v1/contacts";
            context.Request.ContentType="application/json";
            context.Request.Headers.Authorization="Bearer " + issuer.IssueToken(new() { Subject="agent" });
            context.Request.Headers["X-Tenant-Id"]=tenant.ToString();
            if (method != "POST") context.Request.Headers.IfMatch="\"v1\"";
            context.Request.Body=stream;
        },cancel.Token);
        try
        {
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            using(var otherWrite=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Independent\",\"email\":\"independent@example.test\"}").WaitAsync(TimeSpan.FromSeconds(5)))
                Assert.Equal(HttpStatusCode.Created,otherWrite.StatusCode);
            Guid agentId;
            await using (var c=new NpgsqlConnection(db.MigrationConnectionString))
            {
                await c.OpenAsync(); await using var cmd=new NpgsqlCommand("SELECT id FROM platform.memberships WHERE tenant_id=@t AND subject='agent'",c); cmd.Parameters.AddWithValue("t",tenant); agentId=(Guid)(await cmd.ExecuteScalarAsync())!;
            }
            using(var revoked=await Send(HttpMethod.Patch,$"/api/v1/memberships/{agentId}","{\"status\":\"revoked\"}",match:"\"v1\"").WaitAsync(TimeSpan.FromSeconds(5)))
                Assert.Equal(HttpStatusCode.OK,revoked.StatusCode);
            stream.Release.TrySetResult();
            var response=await pending.WaitAsync(TimeSpan.FromSeconds(8));
            Assert.Equal(StatusCodes.Status403Forbidden,response.Response.StatusCode);
            Assert.Equal(0,await Count("crm.timeline_entries",tenant));
            Assert.Equal(method != "POST" ? 2 : 1,await Count("crm.contacts",tenant));
            Assert.Equal(method != "POST" ? 2 : 1,await Count("outbox.events",tenant));
        }
        finally { stream.Release.TrySetResult(); cancel.Cancel(); try { await pending; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task Inflight_crm_write_fences_revocation_without_serializing_independent_writes()
    {
        var id=await CreateContact("target@example.test");
        await using var admin=new NpgsqlConnection(db.MigrationConnectionString); await admin.OpenAsync();
        await using (var setup=new NpgsqlCommand($"CREATE FUNCTION outbox.block_test() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.contact_id='{id}' THEN PERFORM pg_advisory_xact_lock(732061); END IF; RETURN NEW; END $$; CREATE TRIGGER block_test BEFORE INSERT ON outbox.events FOR EACH ROW EXECUTE FUNCTION outbox.block_test(); SELECT pg_advisory_lock(732061)",admin)) await setup.ExecuteNonQueryAsync();
        Guid agentId;
        await using (var query=new NpgsqlCommand("SELECT id FROM platform.memberships WHERE tenant_id=@t AND subject='agent'",admin)) { query.Parameters.AddWithValue("t",tenant); agentId=(Guid)(await query.ExecuteScalarAsync())!; }
        var write=Send(HttpMethod.Patch,$"/api/v1/contacts/{id}","{\"displayName\":\"Updated\"}",subject:"agent",match:"\"v1\"");
        Task<HttpResponseMessage>? revoke=null;
        try
        {
            var writerPid=await FindBlockedPid(admin.ProcessID);
            revoke=Send(HttpMethod.Patch,$"/api/v1/memberships/{agentId}","{\"status\":\"revoked\"}",match:"\"v1\"");
            await FindBlockedPid(writerPid);
            using(var independent=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Independent\",\"email\":\"independent@example.test\"}").WaitAsync(TimeSpan.FromSeconds(8))) Assert.Equal(HttpStatusCode.Created,independent.StatusCode);
            await using (var release=new NpgsqlCommand("SELECT pg_advisory_unlock(732061)",admin)) await release.ExecuteNonQueryAsync();
            using(var result=await write.WaitAsync(TimeSpan.FromSeconds(8))) Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            using(var result=await revoke.WaitAsync(TimeSpan.FromSeconds(8))) Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            using(var denied=await Send(HttpMethod.Patch,$"/api/v1/contacts/{id}","{\"displayName\":\"Denied\"}",subject:"agent",match:"\"v2\"")) await Problem(denied,HttpStatusCode.Forbidden,"TENANT_ACCESS_DENIED");
        }
        finally
        {
            await using var unlock=new NpgsqlCommand("SELECT pg_advisory_unlock_all()",admin); await unlock.ExecuteNonQueryAsync();
            try { (await write.WaitAsync(TimeSpan.FromSeconds(8))).Dispose(); } catch (TimeoutException) { }
            if (revoke is not null) try { (await revoke.WaitAsync(TimeSpan.FromSeconds(8))).Dispose(); } catch (TimeoutException) { }
        }
    }

    private async Task<int> FindBlockedPid(int blocker)
    {
        await using var observer=new NpgsqlConnection(db.MigrationConnectionString); await observer.OpenAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!timeout.IsCancellationRequested)
        {
            await using var query=new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE @blocker=ANY(pg_blocking_pids(pid)) LIMIT 1",observer);
            query.Parameters.AddWithValue("blocker",blocker); var result=await query.ExecuteScalarAsync();
            if (result is int pid) return pid;
            await Task.Delay(40,timeout.Token);
        }
        throw new TimeoutException("Expected database lock wait was not observed.");
    }

    private sealed class SlowBodyStream(string contents) : Stream
    {
        private readonly byte[] data=Encoding.UTF8.GetBytes(contents);
        private int position;
        public TaskCompletionSource Entered { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => data.Length; public override long Position { get => position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
            var count=Math.Min(buffer.Length,data.Length-position); data.AsMemory(position,count).CopyTo(buffer); position+=count; return count;
        }
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset,count),cancellationToken).AsTask();
        public override int Read(byte[] buffer,int offset,int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long offset,SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count) => throw new NotSupportedException();
    }
}
