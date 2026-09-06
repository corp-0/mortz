using Mortz.Core.Replication;

namespace Mortz.Protocol.Replication;

public static class SnapshotCodec
{
    public static byte[] Serialize(this Snapshot snapshot) => SnapshotWire.Serialize(snapshot, null);
    public static byte[] SerializeFor(this Snapshot snapshot, int localPeerId) => SnapshotWire.Serialize(snapshot, localPeerId);
}
