using Mortz.Net;

namespace Mortz.Client.Debug;

/// <summary>Peer 1, so events aimed at the local player still fire.</summary>
public sealed class FakeNetwork : INetwork
{
    public int LocalPeerId => 1;

    public event Action? Connected { add { } remove { } }
    public event Action? ConnectionFailed { add { } remove { } }
    public event Action? Disconnected { add { } remove { } }
    public event Action? TransportReset { add { } remove { } }
    public event Action<byte[], int>? SnapshotReceived { add { } remove { } }

    public void SendInputs(byte[] packet)
    {
    }
}
