using System.Net;
using System.Net.Sockets;

namespace Integration.Fixtures;

// Test-only loopback relay to the fixture's published PostgreSQL port.
// Neither direction inspects or records protocol data.
internal sealed class PostgresReplyGate : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task serving;
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource withheld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int hold;

    public PostgresReplyGate(int postgresPort)
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        serving = ServeAsync(postgresPort);
    }

    public int Port { get; }
    public void HoldReplies() => Volatile.Write(ref hold, 1);
    public Task ReplyWithheld => withheld.Task;
    public void Disconnect() => release.TrySetResult();

    private async Task ServeAsync(int postgresPort)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                using var upstream = new TcpClient();
                await upstream.ConnectAsync(IPAddress.Loopback, postgresPort, lifetime.Token);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var forward = client.GetStream().CopyToAsync(upstream.GetStream(), session.Token);
                var replies = ForwardRepliesAsync(upstream.GetStream(), client.GetStream(), session.Token);
                await Task.WhenAny(forward, replies);
                await session.CancelAsync();
                try { await Task.WhenAll(forward, replies); }
                catch (OperationCanceledException) when (session.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private async Task ForwardRepliesAsync(NetworkStream source, NetworkStream destination, CancellationToken ct)
    {
        var buffer = new byte[8192];
        while (true)
        {
            var length = await source.ReadAsync(buffer, ct);
            if (length == 0) return;
            if (Interlocked.Exchange(ref hold, 0) != 0)
            {
                withheld.TrySetResult();
                await release.Task.WaitAsync(ct);
                return;
            }
            await destination.WriteAsync(buffer.AsMemory(0, length), ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        try { await serving; }
        finally { listener.Stop(); lifetime.Dispose(); }
    }
}
