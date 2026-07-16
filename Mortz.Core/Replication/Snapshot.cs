using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

/// <summary>Authoritative world state at one tick, as sent server -> clients.
/// The binary layout lives in SnapshotWire; this is just the state.</summary>
public sealed record Snapshot(int Tick, PlayerState[] Players, MortarState[] Mortars)
{
    /// <summary>Full records for persistence/tests. Live traffic should call
    /// <see cref="SerializeFor"/> so only the owner's prediction state is sent.</summary>
    public byte[] Serialize() => SnapshotWire.Serialize(this, localPeerId: null);

    public byte[] SerializeFor(int localPeerId) => SnapshotWire.Serialize(this, localPeerId);

    public static Snapshot Deserialize(byte[] data) => SnapshotWire.Deserialize(data, peersBySlot: null);

    public static Snapshot Deserialize(byte[] data, IReadOnlyDictionary<byte, int>? peersBySlot) =>
        SnapshotWire.Deserialize(data, peersBySlot);
}
