namespace Mortz.Net;

/// <summary>What the client sees of the network: local identity, connection
/// lifecycle, and the snapshot/input hot path.</summary>
public interface INetwork
{
    /// <summary>0 means no session (no real peer ever has id 0).</summary>
    int LocalPeerId { get; }

    event Action? Connected;
    event Action? ConnectionFailed;
    event Action? Disconnected;
    event Action? TransportReset;
    event Action<byte[], int>? SnapshotReceived;

    void SendInputs(byte[] packet);
}
