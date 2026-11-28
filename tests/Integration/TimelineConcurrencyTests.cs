using System.Net;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class TimelineConcurrencyTests : DefinitionEndpointFixture
{
    [Theory]
    [InlineData("notes")]
    [InlineData("patch")]
    [InlineData("tag")]
    [InlineData("field")]
    [InlineData("delete")]
    [InlineData("delete-first")]
    public async Task Concurrent_writes_with_same_version_have_one_winner(string rival)
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Timeline"}""");
        var path = $"/api/v1/contacts/{id}";
        var tag = await CreateId("/api/v1/tags", """{"name":"T"}""");
        var field = await CreateId("/api/v1/custom-fields", """{"key":"x","displayName":"X","dataType":"number"}""");
        await using var gate = new NpgsqlConnection(db.AdminConnectionString);
        await gate.OpenAsync();
        await using var tx = await gate.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("SELECT id FROM crm.contacts WHERE tenant_id=$1 AND id=$2 FOR UPDATE", gate, tx);
        cmd.Parameters.AddWithValue(tenant);
        cmd.Parameters.AddWithValue(id);
        Assert.Equal(id, await cmd.ExecuteScalarAsync());
        var version = new[] { "\"v1\"" };
        var first = rival == "delete-first" ? Send(HttpMethod.Delete, path, match: version) : Send(HttpMethod.Post, path + "/notes", """{"text":"one"}""", match: version);
        var blocker = await WaitForBlockedWriter(gate.ProcessID);
        var second = rival switch
        {
            "patch" => Send(HttpMethod.Patch, path, """{"displayName":"New"}""", "agent", match: version),
            "tag" => Send(HttpMethod.Put, $"{path}/tags/{tag}", subject: "agent", match: version),
            "field" => Send(HttpMethod.Put, $"{path}/fields/{field}", """{"value":1}""", "agent", match: version),
            "delete" => Send(HttpMethod.Delete, path, subject: "agent", match: version),
            _ => Send(HttpMethod.Post, path + "/notes", """{"text":"two"}""", "agent", match: version)
        };
        await WaitForBlockedWriter(blocker);
        await tx.CommitAsync();
        using var winner = await first;
        Assert.Equal(rival == "delete-first" ? HttpStatusCode.NoContent : HttpStatusCode.Created, winner.StatusCode);
        await Problem(await second, HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Equal(rival == "delete-first" ? 0 : 1, await Count("crm.timeline_entries", tenant));
        Assert.Equal(2, await Count("outbox.events", tenant));
        Assert.Equal(4, await Count("audit.entries", tenant));
    }
    private async Task<int> WaitForBlockedWriter(int blocker)
    {
        await using var monitor = new NpgsqlConnection(db.AdminConnectionString);
        await monitor.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT pid FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid)) LIMIT 1", monitor);
        cmd.Parameters.Add(new() { Value = blocker });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (true)
        {
            if (await cmd.ExecuteScalarAsync(timeout.Token) is int pid) return pid;
            await Task.Delay(20, timeout.Token);
        }
    }
}
