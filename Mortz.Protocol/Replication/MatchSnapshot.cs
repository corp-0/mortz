using Mortz.Core.Replication;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

public readonly record struct ReplicatedPlayer(
    PlayerState Simulation,
    PlayerPresentationState Presentation, byte Slot = 0, byte Skin = 0);

/// <summary>Simulation and presentation state sampled atomically at one server tick.</summary>
public record MatchSnapshot(
    int Tick,
    ReplicatedPlayer[] Players,
    int Generation = 0,
    int RosterRevision = 0)
{
    public Snapshot SimulationSnapshot =>
        new(Tick, [.. Players.Select(player => player.Simulation)], []);

    public byte[] Serialize() => MatchSnapshotWire.Serialize(this, localPeerId: null);

    public byte[] SerializeFor(int localPeerId) =>
        MatchSnapshotWire.Serialize(this, localPeerId);

    public static MatchSnapshot Deserialize(byte[] data) =>
        MatchSnapshotWire.Deserialize(data, slots: null);

    /// <summary>Remote player slots are resolved through the reliable roster.</summary>
    public static MatchSnapshot Deserialize(byte[] data, IPeerSlots slots) =>
        MatchSnapshotWire.Deserialize(data, slots);
}
