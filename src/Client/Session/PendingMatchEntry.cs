using Mortz.Core.Match.Participation;
using Mortz.Core.Net.Sim;
using Mortz.Shared;

namespace Mortz.Client.Session;

/// <summary>Verified map plus its in-progress terrain/config transfer.</summary>
public sealed class PendingMatchEntry
{
    public int Generation { get; }
    public MapPackage Map { get; }
    public TerrainTransfer Terrain { get; }
    public MatchParticipation Participation { get; }
    public byte[] InitialSnapshot { get; }
    public int InitialSnapshotAck { get; }

    private PendingMatchEntry(int generation, MapPackage map, TerrainTransfer terrain,
        MatchParticipation participation, byte[] initialSnapshot, int initialSnapshotAck)
    {
        Generation = generation;
        Map = map;
        Terrain = terrain;
        Participation = participation;
        InitialSnapshot = initialSnapshot;
        InitialSnapshotAck = initialSnapshotAck;
    }

    public static bool TryCreate(WelcomeMsg welcome, out PendingMatchEntry? bootstrap,
        out string error)
    {
        bootstrap = null;
        // Fresh catalog per join: a map installed since app start must count.
        MapPackage? map = GameContent.Load()?.LoadMap(welcome.MapId);
        if (map == null || map.Hash != welcome.MapHash)
        {
            error = $"Map mismatch: {welcome.MapId}";
            return false;
        }
        if (!TerrainTransfer.TryCreate(welcome, out TerrainTransfer? terrain, out error))
            return false;
        MatchParticipation participation = new(
            welcome.Seat, welcome.Activity, welcome.SpectateReason, welcome.ReturnTick);
        if (!participation.IsValid || welcome.InitialSnapshot.Length == 0)
        {
            error = "Invalid match participation bootstrap.";
            return false;
        }
        bootstrap = new PendingMatchEntry(welcome.Generation, map, terrain!, participation,
            welcome.InitialSnapshot, welcome.InitialSnapshotAck);
        return true;
    }
}
