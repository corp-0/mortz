using Mortz.Core.Net;

namespace Mortz.Core.Replication;

/// <summary>Fixed binary layout for the outer match snapshot.</summary>
public static class MatchSnapshotWire
{
    public static byte[] Serialize(MatchSnapshot snapshot, int? localPeerId)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        SnapshotWire.Write(writer, snapshot.SimulationSnapshot, localPeerId);
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
        Snapshot simulation = SnapshotWire.Read(reader, slots);
        ReplicatedPlayer[] players = new ReplicatedPlayer[simulation.Players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = new ReplicatedPlayer(
                simulation.Players[i],
                ReadPresentation(reader));
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes in match snapshot.");
        return new MatchSnapshot(simulation.Tick, players, simulation.Mortars);
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
