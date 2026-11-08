using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Integration.Fixtures;
using Npgsql;
using Xunit;

namespace Integration;

public sealed class HealthTests
{
    [Fact]
    public async Task Live_is_independent_of_database()
    {
        // Reserve a non-listening port so no database can accept this connection.
        using var unavailable = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        unavailable.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)unavailable.LocalEndPoint!).Port,
            Database = "unreachable",
            Username = "test_user",
            Password = Guid.NewGuid().ToString("N"),
            Timeout = 1,
            Pooling = false
        };
        await using var factory = new ApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = connection.ConnectionString
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "caller-controlled");

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = Assert.Single(body.RootElement.EnumerateObject());
        Assert.Equal("status", status.Name);
        Assert.Equal("live", status.Value.GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var correlation = Assert.Single(response.Headers.GetValues("X-Correlation-Id"));
        Assert.True(Guid.TryParseExact(correlation, "N", out _));
        Assert.NotEqual("caller-controlled", correlation);

        using var second = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.NotEqual(correlation, Assert.Single(second.Headers.GetValues("X-Correlation-Id")));
    }
}
