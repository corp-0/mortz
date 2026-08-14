using Mortz.Core.Net;
using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

public readonly record struct ReplicatedPlayer(
    PlayerState Simulation,
    PlayerPresentationState Presentation);

/// <summary>Simulation and presentation state sampled atomically at one server tick.</summary>
public record MatchSnapshot(
    int Tick,
    ReplicatedPlayer[] Players,
    MortarState[] Mortars)
{
    public Snapshot SimulationSnapshot =>
        new(Tick, [.. Players.Select(player => player.Simulation)], Mortars);

    public byte[] Serialize() => MatchSnapshotWire.Serialize(this, localPeerId: null);

    public byte[] SerializeFor(int localPeerId) =>
        MatchSnapshotWire.Serialize(this, localPeerId);

    public static MatchSnapshot Deserialize(byte[] data) =>
        MatchSnapshotWire.Deserialize(data, slots: null);

    /// <summary>Remote player slots are resolved through the reliable roster.</summary>
    public static MatchSnapshot Deserialize(byte[] data, IPeerSlots slots) =>
        MatchSnapshotWire.Deserialize(data, slots);
}
