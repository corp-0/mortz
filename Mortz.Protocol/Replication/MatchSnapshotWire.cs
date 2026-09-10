using Mortz.Core.Sim;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

/// <summary>Fixed binary layout for the outer match snapshot.</summary>
public static class MatchSnapshotWire
{
    public static byte[] Serialize(MatchSnapshot snapshot, int? localPeerId) =>
        PacketEncoder.Encode(snapshot, localPeerId, Write);

    private static void Write(ref PacketWriter writer, MatchSnapshot snapshot, int? localPeerId)
    {
        writer.Write(snapshot.Generation);
        writer.Write(snapshot.RosterRevision);
        SnapshotWire.WritePlayersHeader(ref writer, snapshot.Tick, snapshot.Players.Length, localPeerId != null);
        for (int index = 0; index < snapshot.Players.Length; index++)
        {
            ReplicatedPlayer player = snapshot.Players[index];
            byte slot = player.Slot == 0 ? (byte)(index + 1) : player.Slot;
            SnapshotWire.WritePlayer(ref writer, player.Simulation, localPeerId, slot);
        }
        foreach (ReplicatedPlayer player in snapshot.Players)
        {
            writer.Write(player.Presentation.KillingSpreeMagnitude);
            writer.Write(player.Presentation.IsBleeding);
            writer.Write(player.Skin);
        }
    }

    public static MatchSnapshot Deserialize(byte[] data, IPeerSlots? slots)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        int generation = reader.ReadInt32();
        int revision = reader.ReadInt32();
        (int tick, PlayerState[] simulationPlayers) = SnapshotWire.ReadPlayers(reader, slots);
        ReplicatedPlayer[] players = new ReplicatedPlayer[simulationPlayers.Length];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new ReplicatedPlayer(
                simulationPlayers[i],
                ReadPresentation(reader), Skin: reader.ReadByte());
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in match snapshot.");
        return new MatchSnapshot(tick, players, generation, revision);
    }

    private static PlayerPresentationState ReadPresentation(BinaryReader reader)
    {
        return PlayerPresentationState.ReadFrom(reader);
    }
}
