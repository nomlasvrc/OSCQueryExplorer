using System.Net;
using System.Net.Sockets;

namespace OSCQueryExplorer.Protocol.Osc;

public sealed record OscPacketReceived(OscPacket Packet, IPEndPoint RemoteEndpoint, IPEndPoint LocalEndpoint, DateTimeOffset Timestamp);

public sealed class UdpOscTransport : IAsyncDisposable
{
    private UdpClient? _receiver;
    private UdpClient? _sender;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    public IPEndPoint? ReceiveEndpoint => _receiver?.Client.LocalEndPoint as IPEndPoint;
    public IPEndPoint? SendEndpoint => _sender?.Client.LocalEndPoint as IPEndPoint;
    public event EventHandler<OscPacketReceived>? PacketReceived;
    public event EventHandler<Exception>? ReceiveFaulted;

    public async Task RunReceiverAsync(IPAddress address, CancellationToken cancellationToken)
    {
        var receiver = new UdpClient(new IPEndPoint(address, 0));
        Interlocked.Exchange(ref _receiver, receiver)?.Dispose();
        var local = (IPEndPoint)receiver.Client.LocalEndPoint!;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var received = await receiver.ReceiveAsync(cancellationToken);
                var packet = OscCodec.Decode(received.Buffer);
                PacketReceived?.Invoke(this, new OscPacketReceived(packet, received.RemoteEndPoint, local, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception) when (exception is SocketException or OscProtocolException) { ReceiveFaulted?.Invoke(this, exception); }
        }
    }

    public async Task<IPEndPoint> SendAsync(OscPacket packet, IPEndPoint destination, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (_sender?.Client.AddressFamily != destination.AddressFamily)
            {
                _sender?.Dispose();
                _sender = new UdpClient(new IPEndPoint(destination.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));
            }
            var sender = _sender;
            var bytes = OscCodec.Encode(packet);
            await sender!.SendAsync(bytes, destination, cancellationToken);
            return (IPEndPoint)sender.Client.LocalEndPoint!;
        }
        finally { _sendGate.Release(); }
    }

    public void CloseSockets()
    {
        _receiver?.Dispose();
        _receiver = null;
        _sender?.Dispose();
        _sender = null;
    }

    public async ValueTask DisposeAsync()
    {
        CloseSockets();
        await _sendGate.WaitAsync();
        _sendGate.Dispose();
    }
}
