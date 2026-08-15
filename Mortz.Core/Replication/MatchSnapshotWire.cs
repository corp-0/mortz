using Mortz.Core.Net;
using Mortz.Core.Sim;

namespace Mortz.Core.Replication;

/// <summary>Fixed binary layout for the outer match snapshot.</summary>
public static class MatchSnapshotWire
{
    public static byte[] Serialize(MatchSnapshot snapshot, int? localPeerId)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        SnapshotWire.WritePlayers(writer, snapshot.Tick,
            [.. snapshot.Players.Select(player => player.Simulation)], localPeerId);
        foreach (ReplicatedPlayer player in snapshot.Players)
        {
            WritePresentation(writer, player.Presentation);
        }
        return stream.ToArray();
    }

    public static MatchSnapshot Deserialize(byte[] data, IPeerSlots? slots)
    {
        using MemoryStream stream = new(data, writable: false);
        using BinaryReader reader = new(stream);
        (int tick, PlayerState[] simulationPlayers) = SnapshotWire.ReadPlayers(reader, slots);
        ReplicatedPlayer[] players = new ReplicatedPlayer[simulationPlayers.Length];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new ReplicatedPlayer(
                simulationPlayers[i],
                ReadPresentation(reader));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in match snapshot.");
        return new MatchSnapshot(tick, players);
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
