using System.Net;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class TagFieldConcurrencyTests : DefinitionEndpointFixture
{
    [Theory]
    [InlineData("field-tag")]
    [InlineData("field-field")]
    [InlineData("field-patch")]
    [InlineData("field-delete")]
    [InlineData("delete-field")]
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
        var involvesField = competitor.Contains("field");
        var field = involvesField ? await CreateId("/api/v1/custom-fields", """{"key":"x","displayName":"X","dataType":"number"}""") : Guid.Empty;
        var fieldPath = $"{path}/fields/{field}";
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
        var deleteFirst = competitor is "delete-first" or "delete-field";
        var first = competitor switch
        {
            "delete-first" or "delete-field" => Send(HttpMethod.Delete, path, match: [version]),
            _ when involvesField => Send(HttpMethod.Put, fieldPath, """{"value":12}""", match: [version]),
            _ => Send(HttpMethod.Put, $"{path}/tags/{tag}", match: [version])
        };
        var pid = await WaitForBlockedWriter(gate.ProcessID);
        var rival = competitor switch
        {
            "field-field" or "delete-field" => Send(HttpMethod.Put, fieldPath, """{"value":12}""", subject: "agent", match: [version]),
            "delete-first" or "field-tag" => Send(HttpMethod.Put, $"{path}/tags/{tag}", subject: "agent", match: [version]),
            "attach" => Send(HttpMethod.Put, $"{path}/tags/{second}", subject: "agent", match: [version]),
            "detach" => Send(HttpMethod.Delete, $"{path}/tags/{second}", subject: "agent", match: [version]),
            "patch" or "field-patch" => Send(HttpMethod.Patch, path, """{"displayName":"Changed"}""", "agent", match: [version]),
            _ => Send(HttpMethod.Delete, path, subject: "agent", match: [version])
        };
        await WaitForBlockedWriter(pid);
        await transaction.CommitAsync();
        using var win = await first;
        using var lose = await rival;
        Assert.Equal(deleteFirst ? HttpStatusCode.NoContent : HttpStatusCode.OK, win.StatusCode);
        await Problem(lose, HttpStatusCode.PreconditionFailed, "VERSION_CONFLICT");
        Assert.Equal(competitor == "detach" ? 3 : 2, await Count("outbox.events", tenant));
        Assert.Equal(competitor == "detach" || involvesField ? 5 : 4, await Count("audit.entries", tenant));
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
