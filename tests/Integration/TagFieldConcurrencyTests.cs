using System.Net;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class TagFieldConcurrencyTests : DefinitionEndpointFixture
{
    [Theory]
    [InlineData("attach")]
    [InlineData("detach")]
    [InlineData("patch")]
    [InlineData("delete")]
    [InlineData("delete-first")]
    public async Task Contact_writers_queued_at_the_same_version_have_one_winner(string competitor)
    {
        var id = await CreateId("/api/v1/contacts", """{"displayName":"Original"}""");
        var path = $"/api/v1/contacts/{id}";
        var tag = await CreateId("/api/v1/tags", """{"name":"A"}""");
        var second = await CreateId("/api/v1/tags", """{"name":"B"}""");
        var version = "\"v1\"";
        if (competitor == "detach")
        {
            using var seed = await Send(HttpMethod.Put, $"{path}/tags/{second}", match: [version]);
            Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
            version = "\"v2\"";
        }
        await using var gate = new NpgsqlConnection(db.AdminConnectionString);
        await gate.OpenAsync();
        await using var transaction = await gate.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM crm.contacts WHERE tenant_id=$1 AND id=$2 FOR UPDATE", gate, transaction);
        command.Parameters.Add(new() { Value = tenant });
        command.Parameters.Add(new() { Value = id });
        Assert.Equal(id, await command.ExecuteScalarAsync());
        var deleteFirst = competitor == "delete-first";
        var first = deleteFirst ? Send(HttpMethod.Delete, path, match: [version]) : Send(HttpMethod.Put, $"{path}/tags/{tag}", subject: "owner", match: [version]);
        var pid = await WaitForBlockedWriter(gate.ProcessID);
        var rival = competitor switch
        {
            "delete-first" => Send(HttpMethod.Put, $"{path}/tags/{tag}", subject: "agent", match: [version]),
            "attach" => Send(HttpMethod.Put, $"{path}/tags/{second}", subject: "agent", match: [version]),
            "detach" => Send(HttpMethod.Delete, $"{path}/tags/{second}", subject: "agent", match: [version]),
            "patch" => Send(HttpMethod.Patch, path, """{"displayName":"Changed"}""", "agent", match: [version]),
            _ => Send(HttpMethod.Delete, path, subject: "agent", match: [version])
        };
        await WaitForBlockedWriter(pid);
        await transaction.CommitAsync();
        using var win = await first;
        using var lose = await rival;
        Assert.Equal(deleteFirst ? HttpStatusCode.NoContent : HttpStatusCode.OK, win.StatusCode);
        await Problem(lose, HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Equal(competitor == "detach" ? 3 : 2, await Count("outbox.events", tenant));
        Assert.Equal(competitor == "detach" ? 5 : 4, await Count("audit.entries", tenant));
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
