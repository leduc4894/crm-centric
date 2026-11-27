using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Npgsql;
using Platform.Infrastructure.Persistence;
using Xunit;

namespace Integration;

public sealed partial class ContactListTests : IAsyncLifetime
{
    private readonly PostgresFixture db = new();
    private readonly Guid tenant = Guid.NewGuid(), other = Guid.NewGuid();
    private FakeOidcIssuer issuer = null!;
    private ApiFactory factory = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        await db.InitializeAsync(); issuer = await FakeOidcIssuer.StartAsync();
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync();
        await using var ctx = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(c, p => p.MigrationsHistoryTable(PlatformDbContext.HistoryTable, PlatformDbContext.HistorySchema)).Options);
        await ctx.Database.MigrateAsync(); await DatabaseRoleGrants.ApplyAsync(c, db.ApplicationRole, default);
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO platform.tenants VALUES (@t,'A','active',now()),(@o,'B','active',now());
            INSERT INTO platform.memberships VALUES
                (@t,gen_random_uuid(),@iss,'owner','owner','active',1),(@t,gen_random_uuid(),@iss,'admin','admin','active',1),
                (@t,gen_random_uuid(),@iss,'agent','agent','active',1),(@t,gen_random_uuid(),@iss,'operator','operator','active',1),
                (@t,gen_random_uuid(),@iss,'revoked','agent','inactive',1),(@o,gen_random_uuid(),@iss,'other','owner','active',1);
            INSERT INTO platform.staff_bindings SELECT @iss,s,'active','ticket' FROM unnest(ARRAY['owner','admin','agent','operator','revoked','other','outsider']) s;
            """,c);
        cmd.Parameters.AddWithValue("t",tenant); cmd.Parameters.AddWithValue("o",other); cmd.Parameters.AddWithValue("iss",issuer.Issuer.AbsoluteUri); await cmd.ExecuteNonQueryAsync();
        factory = new ApiFactory(new Dictionary<string,string?> { ["ConnectionStrings:Platform"] = db.ApplicationConnectionString, ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri, ["Oidc:AllowHttpLoopback"] = "true" });
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client.Dispose(); await factory.DisposeAsync(); await issuer.DisposeAsync(); await db.DisposeAsync(); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? body = null, string subject = "owner", string? selected = null, bool anonymous = false, string? match = null, string? contactId = null)
    {
        using var req = new HttpRequestMessage(method,path);
        if (!anonymous) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() { Subject=subject, Role="owner" }));
        req.Headers.Add("X-Tenant-Id", selected ?? tenant.ToString());
        if (body is not null) req.Content = new StringContent(body,Encoding.UTF8,"application/json");
        if (match is not null) req.Headers.TryAddWithoutValidation("If-Match",match);
        if (contactId is not null) req.Headers.TryAddWithoutValidation("X-Contact-Id",contactId);
        return await client.SendAsync(req);
    }
    private static async Task Problem(HttpResponseMessage r,HttpStatusCode status,string reason)
    {
        Assert.Equal(status,r.StatusCode); Assert.Equal("application/problem+json",r.Content.Headers.ContentType!.MediaType);
        using var doc=JsonDocument.Parse(await r.Content.ReadAsStringAsync()); Assert.Equal(reason,doc.RootElement.GetProperty("reasonToken").GetString());
        Assert.Equal(r.Headers.GetValues("X-Correlation-Id").Single(),doc.RootElement.GetProperty("correlationId").GetString());
    }
    private async Task<long> Count(string table, Guid t)
    {
        await using var c = new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var tx = await c.BeginTransactionAsync();
        await using var setting = new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx); setting.Parameters.AddWithValue("t",t.ToString()); await setting.ExecuteNonQueryAsync();
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}",c,tx); return (long)(await cmd.ExecuteScalarAsync())!;
    }
    private async Task Seed(int count, Guid? forTenant=null)
    {
        await using var c=new NpgsqlConnection(db.MigrationConnectionString); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
        await using var setting=new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx); setting.Parameters.AddWithValue("t",(forTenant??tenant).ToString()); await setting.ExecuteNonQueryAsync();
        await using var cmd=new NpgsqlCommand("INSERT INTO crm.contacts (tenant_id,id,display_name,created_at,updated_at) SELECT @t,gen_random_uuid(),'Contact ' || n, '2026-01-01 UTC','2026-01-01 UTC' FROM generate_series(1,@count) n",c,tx); cmd.Parameters.AddWithValue("t",forTenant??tenant); cmd.Parameters.AddWithValue("count",count); await cmd.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }
    private static async Task<(Guid[] ids,string? cursor)> Page(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK,response.StatusCode); using var j=JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var root=j.RootElement;
        Assert.Equal(new[]{"items","nextCursor"},root.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).ToArray());
        foreach(var item in root.GetProperty("items").EnumerateArray()) Assert.Equal(new[]{"createdAt","displayName","email","id","phone","updatedAt","version"},item.EnumerateObject().Select(x=>x.Name).Order(StringComparer.Ordinal).ToArray());
        return (root.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()).ToArray(),root.GetProperty("nextCursor").GetString());
    }
    [Theory]
    [InlineData(0)][InlineData(1)][InlineData(20)][InlineData(21)][InlineData(100)][InlineData(101)]
    public async Task Paging_visits_all_same_timestamp_rows_once(int count)
    {
        await Seed(count); await Seed(3,other); var found=new HashSet<Guid>(); string? cursor=null;
        do { using var r=await Send(HttpMethod.Get,"/api/v1/contacts?limit=20"+(cursor is null?"":"&cursor="+Uri.EscapeDataString(cursor))); var p=await Page(r); foreach(var id in p.ids) Assert.True(found.Add(id)); cursor=p.cursor; } while(cursor is not null);
        Assert.Equal(count,found.Count); Assert.Equal(0,await Count("outbox.events",tenant));
    }
    [Fact]
    public async Task Invalid_parameters_and_cursor_are_correlated_and_private()
    {
        foreach(var path in new[]{"?limit=0","?limit=101","?limit=01","?q=","?q=a&q=b","?unknown=secret","?tagId=ABC","?updatedSince=no","?q=%00","?q=%FF"})
        { using var r=await Send(HttpMethod.Get,"/api/v1/contacts"+path); await Problem(r,HttpStatusCode.BadRequest,"REQUEST_INVALID"); Assert.DoesNotContain("secret",await r.Content.ReadAsStringAsync()); }
        using var bad=await Send(HttpMethod.Get,"/api/v1/contacts?cursor=bad"); await Problem(bad,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using var empty=await Send(HttpMethod.Get,"/api/v1/contacts?cursor="); await Problem(empty,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using var duplicate=await Send(HttpMethod.Get,"/api/v1/contacts?cursor=&cursor=bad"); await Problem(duplicate,HttpStatusCode.BadRequest,"REQUEST_INVALID");
        using var oversized=await Send(HttpMethod.Get,"/api/v1/contacts?cursor="+new string('a',2049)); await Problem(oversized,HttpStatusCode.BadRequest,"CURSOR_INVALID");
    }
    [Fact]
    public async Task Cursor_survives_anchor_deletion_and_binds_tenant_and_filter()
    {
        await Seed(4); using var first=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1"); var page=await Page(first); Assert.NotNull(page.cursor);
        var mutated=(page.cursor![0]=='A'?"B":"A")+page.cursor[1..];
        using(var tampered=await Send(HttpMethod.Get,"/api/v1/contacts?cursor="+mutated)) await Problem(tampered,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using(var wrong=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1&q=x&cursor="+page.cursor)) await Problem(wrong,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using(var wrong=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1&cursor="+page.cursor,subject:"other",selected:other.ToString())) await Problem(wrong,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        using(var del=await Send(HttpMethod.Delete,"/api/v1/contacts/"+page.ids[0],match:"\"v1\"")) Assert.Equal(HttpStatusCode.NoContent,del.StatusCode);
        using var second=await Send(HttpMethod.Get,"/api/v1/contacts?limit=10&cursor="+page.cursor); var p=await Page(second); Assert.Equal(3,p.ids.Length); Assert.DoesNotContain(page.ids[0],p.ids); Assert.Null(p.cursor);
    }
    [Fact]
    public async Task Read_roles_and_tenant_authorization_are_enforced()
    {
        await Seed(1); foreach(var role in new[]{"owner","admin","agent","operator"}) { using var r=await Send(HttpMethod.Get,"/api/v1/contacts",subject:role); Assert.Single((await Page(r)).ids); }
        using(var no=await Send(HttpMethod.Get,"/api/v1/contacts",subject:"outsider")) Assert.Equal(HttpStatusCode.Forbidden,no.StatusCode);
        using(var revoked=await Send(HttpMethod.Get,"/api/v1/contacts",subject:"revoked")) Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
    }
    [Fact]
    public async Task Literal_search_tag_and_timestamp_combine_without_cross_tenant_joins()
    {
        var tag=Guid.NewGuid(); var a=Guid.NewGuid(); var b=Guid.NewGuid(); var foreign=Guid.NewGuid();
        await using(var c=new NpgsqlConnection(db.MigrationConnectionString))
        {
            await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync();
            foreach(var t in new[]{tenant,other})
            {
                await using var setting=new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx); setting.Parameters.AddWithValue("t",t.ToString()); await setting.ExecuteNonQueryAsync();
                await using var insert=new NpgsqlCommand("""
                    INSERT INTO crm.tags(tenant_id,id,name,normalized_name) VALUES (@t,@tag,'Tag','tag');
                    INSERT INTO crm.contacts(tenant_id,id,display_name,email,created_at,updated_at) VALUES (@t,@id,@name,NULL,'2026-01-01 UTC','2026-01-01 UTC');
                    INSERT INTO crm.contact_tags VALUES (@t,@id,@tag);
                    """,c,tx);
                insert.Parameters.AddWithValue("t",t);insert.Parameters.AddWithValue("tag",tag);insert.Parameters.AddWithValue("id",t==tenant?a:foreign);insert.Parameters.AddWithValue("name",t==tenant?"literal %_\\ Ω":"foreign"); await insert.ExecuteNonQueryAsync();
            }
            await using(var setting=new NpgsqlCommand("SELECT set_config('crm.tenant_id',@t,true)",c,tx)) {setting.Parameters.AddWithValue("t",tenant.ToString());await setting.ExecuteNonQueryAsync();}
            await using(var insert=new NpgsqlCommand("INSERT INTO crm.contacts(tenant_id,id,display_name,created_at,updated_at) VALUES (@t,@id,'literal abc Ω','2026-01-01 UTC','2026-01-01 UTC')",c,tx)) {insert.Parameters.AddWithValue("t",tenant);insert.Parameters.AddWithValue("id",b);await insert.ExecuteNonQueryAsync();}
            await tx.CommitAsync();
        }
        foreach(var q in new[]{"%_\\","Ω","literal %_\\","' OR 1=1 --"})
        {
            using var r=await Send(HttpMethod.Get,"/api/v1/contacts?q="+Uri.EscapeDataString(q)+"&tagId="+tag+"&updatedSince=2026-01-01T00%3A00%3A00Z"); var ids=(await Page(r)).ids;
            if(q.StartsWith("'")) Assert.Empty(ids); else Assert.Equal(new[]{a},ids);
        }
        using(var newer=await Send(HttpMethod.Get,"/api/v1/contacts?tagId="+tag+"&updatedSince=2026-01-01T00%3A00%3A00.000001Z")) Assert.Empty((await Page(newer)).ids);
        using(var absent=await Send(HttpMethod.Get,"/api/v1/contacts?tagId="+Guid.NewGuid())) Assert.Empty((await Page(absent)).ids);
        using(var foreignPage=await Send(HttpMethod.Get,"/api/v1/contacts?tagId="+tag,subject:"other",selected:other.ToString())) Assert.Equal(new[]{foreign},(await Page(foreignPage)).ids);
    }
    [Fact]
    public async Task Missing_key_and_unavailable_database_fail_closed()
    {
        foreach(var brokenDatabase in new[]{false,true})
        {
            var settings=new Dictionary<string,string?> { ["ConnectionStrings:Platform"] = brokenDatabase ? "Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Timeout=1" : db.ApplicationConnectionString,
                ["Oidc:Issuer"] = issuer.Issuer.AbsoluteUri, ["Oidc:Audience"] = "crm-api", ["Oidc:MetadataAddress"] = new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri,
                ["Oidc:AllowHttpLoopback"] = "true" };
            if(!brokenDatabase) settings["ContactCursor:SigningKey"]="";
            await using var broken=new ApiFactory(settings); using var http=broken.CreateClient();
            using var request=new HttpRequestMessage(HttpMethod.Get,"/api/v1/contacts"); request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() {Subject="owner"})); request.Headers.Add("X-Tenant-Id",tenant.ToString());
            using var response=await http.SendAsync(request); await Problem(response,HttpStatusCode.ServiceUnavailable,brokenDatabase?"CRM_STORE_UNAVAILABLE":"CRM_CURSOR_UNAVAILABLE");
        }
    }
    [Fact]
    public async Task Concurrent_update_does_not_imply_snapshot_and_remains_tenant_scoped()
    {
        await Seed(3); await Seed(1,other);
        using var first=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1"); var page=await Page(first);
        using var next=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1&cursor="+page.cursor); var candidate=await Page(next);
        using(var patch=await Send(HttpMethod.Patch,"/api/v1/contacts/"+candidate.ids[0],"{\"displayName\":\"Moved\"}",match:"\"v1\"")) Assert.Equal(HttpStatusCode.OK,patch.StatusCode);
        using var after=await Send(HttpMethod.Get,"/api/v1/contacts?limit=10&cursor="+page.cursor); var remaining=await Page(after);
        Assert.Single(remaining.ids); Assert.DoesNotContain(candidate.ids[0],remaining.ids); Assert.Null(remaining.cursor);
    }
    [Fact]
    public async Task Page_size_can_change_and_upper_limit_returns_100()
    {
        await Seed(101); using var first=await Send(HttpMethod.Get,"/api/v1/contacts?limit=100"); var page=await Page(first); Assert.Equal(100,page.ids.Length); Assert.NotNull(page.cursor);
        using var last=await Send(HttpMethod.Get,"/api/v1/contacts?limit=1&cursor="+page.cursor); var end=await Page(last); Assert.Single(end.ids); Assert.DoesNotContain(end.ids[0],page.ids); Assert.Null(end.cursor);
    }
    [Fact]
    public async Task Listing_does_not_write_audit_or_outbox()
    {
        using(var created=await Send(HttpMethod.Post,"/api/v1/contacts","{\"displayName\":\"Existing\"}")) Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var audit=await Count("audit.entries",tenant); var outbox=await Count("outbox.events",tenant);
        Assert.Equal(1,audit); Assert.Equal(1,outbox);
        using(var response=await Send(HttpMethod.Get,"/api/v1/contacts?q=Existing")) Assert.Single((await Page(response)).ids);
        using(var invalid=await Send(HttpMethod.Get,"/api/v1/contacts?cursor=bad")) await Problem(invalid,HttpStatusCode.BadRequest,"CURSOR_INVALID");
        Assert.Equal(audit,await Count("audit.entries",tenant)); Assert.Equal(outbox,await Count("outbox.events",tenant));
    }

    private Dictionary<string,string?> InstanceSettings(string key) => new() {
        ["ConnectionStrings:Platform"]=db.ApplicationConnectionString, ["Oidc:Issuer"]=issuer.Issuer.AbsoluteUri,
        ["Oidc:Audience"]="crm-api", ["Oidc:MetadataAddress"]=new Uri(issuer.Issuer,".well-known/openid-configuration").AbsoluteUri,
        ["Oidc:AllowHttpLoopback"]="true", ["ContactCursor:SigningKey"]=key
    };
    private async Task<HttpResponseMessage> SendThrough(HttpClient http,string path)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,path);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",issuer.IssueToken(new() {Subject="owner"}));
        request.Headers.Add("X-Tenant-Id",tenant.ToString()); return await http.SendAsync(request);
    }

    [Fact]
    public async Task Cursor_is_accepted_across_instances_with_same_key_but_not_different_key()
    {
        await Seed(3); var shared=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); var different=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var f1=new ApiFactory(InstanceSettings(shared)); await using var f2=new ApiFactory(InstanceSettings(shared)); await using var f3=new ApiFactory(InstanceSettings(different));
        using var c1=f1.CreateClient(); using var c2=f2.CreateClient(); using var c3=f3.CreateClient();
        using var first=await SendThrough(c1,"/api/v1/contacts?limit=1"); var page=await Page(first); Assert.NotNull(page.cursor);
        using(var same=await SendThrough(c2,"/api/v1/contacts?limit=1&cursor="+page.cursor)) { var result=await Page(same); Assert.Single(result.ids); Assert.DoesNotContain(result.ids[0],page.ids); }
        using(var wrong=await SendThrough(c3,"/api/v1/contacts?limit=1&cursor="+page.cursor)) await Problem(wrong,HttpStatusCode.BadRequest,"CURSOR_INVALID");
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; }=new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
        public void Dispose() { }
        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level,EventId id,TState state,Exception? exception,Func<TState,Exception?,string> formatter) => messages.Enqueue(formatter(state,exception));
        }
    }

    [Fact]
    public async Task Http_errors_do_not_reflect_or_log_query_or_cursor()
    {
        const string search="private-search-sentinel"; const string cursor="private-cursor-sentinel";
        var capture=new CaptureProvider();
        using var instrumented=factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var http=instrumented.CreateClient();
        // Positive control verifies the in-memory provider actually receives host logs.
        instrumented.Services.GetRequiredService<ILoggerFactory>().CreateLogger("privacy-test").LogWarning("capture-positive-control");
        using var response=await SendThrough(http,"/api/v1/contacts?q="+search+"&cursor="+cursor);
        await Problem(response,HttpStatusCode.BadRequest,"CURSOR_INVALID"); var content=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(search,content); Assert.DoesNotContain(cursor,content);
        Assert.Contains(capture.Messages,message => message.Contains("capture-positive-control",StringComparison.Ordinal));
        Assert.DoesNotContain(capture.Messages,message => message.Contains(search,StringComparison.Ordinal) || message.Contains(cursor,StringComparison.Ordinal));
    }
}
