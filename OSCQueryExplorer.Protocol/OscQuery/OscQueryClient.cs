using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using OSCQueryExplorer.Core.Models;

namespace OSCQueryExplorer.Protocol.OscQuery;

public sealed class OscQueryClient(HttpClient? httpClient = null) : IAsyncDisposable
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    private readonly bool _ownsHttpClient = httpClient is null;
    private ClientWebSocket? _webSocket;
    public event EventHandler<string>? PathChanged;
    public event EventHandler<byte[]>? BinaryPacketReceived;
    public event EventHandler<Exception>? WebSocketFaulted;

    public async Task<OscQueryHostInfo> GetHostInfoAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new UriBuilder(endpoint) { Query = "HOST_INFO" }.Uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return OscQueryDocumentParser.ParseHostInfo(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task<OscNode> GetTreeAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(new Uri(endpoint, "/"), cancellationToken);
        response.EnsureSuccessStatusCode();
        return OscQueryDocumentParser.ParseTree(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public async Task RunWebSocketAsync(Uri endpoint, OscQueryHostInfo hostInfo, CancellationToken cancellationToken)
    {
        var scheme = endpoint.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
        var builder = new UriBuilder(scheme, hostInfo.WebSocketIp ?? endpoint.Host, hostInfo.WebSocketPort ?? endpoint.Port, endpoint.AbsolutePath);
        var delay = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var socket = new ClientWebSocket();
                Interlocked.Exchange(ref _webSocket, socket)?.Dispose();
                await socket.ConnectAsync(builder.Uri, cancellationToken);
                delay = TimeSpan.FromSeconds(1);
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                WebSocketFaulted?.Invoke(this, exception);
                try { await Task.Delay(delay, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) break;
            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;
            var bytes = message.ToArray(); message.SetLength(0);
            if (result.MessageType == WebSocketMessageType.Binary) BinaryPacketReceived?.Invoke(this, bytes);
            else HandleCommand(Encoding.UTF8.GetString(bytes));
        }
    }

    private void HandleCommand(string text)
    {
        try
        {
            var json = JsonNode.Parse(text)?.AsObject();
            if (json?["COMMAND"]?.GetValue<string>() == "PATH_CHANGED" && json["DATA"]?.GetValue<string>() is { } path) PathChanged?.Invoke(this, path);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException) { WebSocketFaulted?.Invoke(this, exception); }
    }

    public void AbortWebSocket()
    {
        var socket = Interlocked.Exchange(ref _webSocket, null);
        socket?.Abort();
        socket?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        AbortWebSocket();
        if (_ownsHttpClient) _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
