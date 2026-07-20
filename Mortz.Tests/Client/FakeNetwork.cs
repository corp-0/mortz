using Mortz.Net;

namespace Mortz.Tests.Client;

/// <summary>Test-driven INetwork: set the id, raise the events.</summary>
public sealed class FakeNetwork : INetwork
{
    public int LocalPeerId { get; set; }

    public List<byte[]> SentInputs { get; } = [];

    public event Action? Connected;
    public event Action? ConnectionFailed;
    public event Action? Disconnected;
    public event Action? TransportReset;
    public event Action<byte[], int>? SnapshotReceived;

    public void SendInputs(byte[] packet) => SentInputs.Add(packet);

    public void RaiseConnected() => Connected?.Invoke();
    public void RaiseConnectionFailed() => ConnectionFailed?.Invoke();
    public void RaiseDisconnected() => Disconnected?.Invoke();
    public void RaiseTransportReset() => TransportReset?.Invoke();
    public void RaiseSnapshot(byte[] data, int ack) => SnapshotReceived?.Invoke(data, ack);
}
