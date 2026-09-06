using Mortz.Core.Sim;
using Mortz.Protocol.Net;

namespace Mortz.Protocol.Replication;

/// <summary>Fixed binary layout for the outer match snapshot.</summary>
public static class MatchSnapshotWire
{
    public static byte[] Serialize(MatchSnapshot snapshot, int? localPeerId)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(snapshot.Generation);
        writer.Write(snapshot.RosterRevision);
        SnapshotWire.WritePlayers(writer, snapshot.Tick,
            [.. snapshot.Players.Select(player => player.Simulation)], localPeerId,
            snapshot.Players.Select((player, index) => (player, index)).ToDictionary(
                item => item.player.Simulation.PeerId, item => item.player.Slot == 0 ? (byte)(item.index + 1) : item.player.Slot));
        foreach (ReplicatedPlayer player in snapshot.Players)
        {
            WritePresentation(writer, player.Presentation);
            writer.Write(player.Skin);
        }
        return stream.ToArray();
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

    private static void WritePresentation(BinaryWriter writer, in PlayerPresentationState presentation)
    {
        PlayerPresentationState.WriteTo(writer, presentation);
    }

    private static PlayerPresentationState ReadPresentation(BinaryReader reader)
    {
        return PlayerPresentationState.ReadFrom(reader);
    }
}
